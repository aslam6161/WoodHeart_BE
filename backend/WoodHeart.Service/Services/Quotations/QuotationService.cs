using System.Text.Json;
using Microsoft.Extensions.Logging;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Entity.Quotations;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Enums.Quotations;
using WoodHeart.Domain.Helpers;
using WoodHeart.Domain.Ordering;
using WoodHeart.Domain.Pricing;
using WoodHeart.Domain.Quotations;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Repository.Interfaces.Ordering;
using WoodHeart.Repository.Interfaces.Quotations;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.DTOs.Quotations;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Inventory;
using WoodHeart.Service.Interfaces.Notifications;
using WoodHeart.Service.Interfaces.Ordering;
using WoodHeart.Service.Interfaces.Payments;
using WoodHeart.Service.Interfaces.Quotations;
using WoodHeart.Service.Mapping.Quotations;
using WoodHeart.Service.Services.Common;
using WoodHeart.Service.Services.Notifications;

namespace WoodHeart.Service.Services.Quotations;

/// <inheritdoc cref="IQuotationService" />
/// <remarks>
/// <para>
/// <b>The arithmetic is <c>CartPricer</c>'s, the same function that prices a
/// basket and an order.</b> A quotation is a bill like any other: lines, a
/// discount, VAT, delivery. Writing a second pricer for it would be writing a
/// second set of VAT rules, and the day they disagreed the shop would find out
/// from a customer.
/// </para>
/// <para>
/// <b>Converting does not re-price.</b> A quotation is a promise of a figure
/// and honouring it is the whole point of having given it, so the order takes
/// the quoted money as it stands — down to the VAT rate, which is copied rather
/// than re-read in case the shop changed it in between.
/// </para>
/// <para>
/// <b>A guest is the ordinary case again.</b> Number plus phone, exactly as an
/// order and a booking, and a wrong pair is answered the same as a number that
/// does not exist.
/// </para>
/// </remarks>
public class QuotationService(
    IQuotationRepository quotations,
    IOrderRepository orders,
    IProductVariantRepository variants,
    IPricingContextFactory pricing,
    IPaymentProviderResolver payments,
    INumberSequenceService numbers,
    INotificationQueue notifications,
    IInventoryService inventory,
    IStoreSettingService settings,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    ILogger<QuotationService> logger) : IQuotationService
{
    // -------------------------------------------------------------------------
    // Reading
    // -------------------------------------------------------------------------

    public async Task<GeneralResponse<PagedResult<QuotationListItemDto>>> SearchAsync(
        QuotationQueryDto query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = await quotations.SearchAsync(
            new QuotationSearch(
                query.Term,
                query.Status,
                query.BookingId,
                Math.Max(query.Page, 1),
                Math.Clamp(query.PageSize, 1, QuotationRules.MaxPageSize)),
            cancellationToken);

        var today = clock.DhakaToday;

        return GeneralResponse<PagedResult<QuotationListItemDto>>.Success(
            new PagedResult<QuotationListItemDto>
            {
                Items = [.. page.Select(row => QuotationMapper.ToListItem(row, today))],
                Total = page.TotalCount,
                Page = page.CurrentPage,
                PageSize = page.PageSize
            });
    }

    public async Task<GeneralResponse<QuotationDto>> GetForStaffAsync(
        string quotationNumber, CancellationToken cancellationToken = default)
    {
        var quotation = await quotations.GetByNumberAsync(quotationNumber, cancellationToken);

        return quotation is null
            ? Fail(QuotationErrors.NotFound, "We could not find that quotation.")
            : Ok(quotation, forStaff: true);
    }

    public async Task<GeneralResponse<QuotationDto>> GetAsync(
        string quotationNumber, string? contactPhone, CancellationToken cancellationToken = default)
    {
        var quotation = await quotations.GetByNumberAsync(quotationNumber, cancellationToken);

        // A draft is the designer's working copy. To the customer it does not
        // exist yet, and saying so any other way would be showing them figures
        // nobody has agreed to.
        return quotation is null
               || quotation.Status == QuotationStatus.Draft
               || !MayView(quotation, contactPhone)
            ? Fail(QuotationErrors.NotFound, "We could not find that quotation.")
            : Ok(quotation, forStaff: false);
    }

    public async Task<GeneralResponse<PagedResult<QuotationDto>>> GetMineAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } customerId)
        {
            return GeneralResponse<PagedResult<QuotationDto>>.Fail(
                QuotationErrors.NotFound, "Please sign in to see your quotations.");
        }

        var size = Math.Clamp(pageSize, 1, QuotationRules.MaxPageSize);
        var current = Math.Max(page, 1);
        var today = clock.DhakaToday;

        var rows = await quotations.GetForCustomerAsync(
            customerId, (current - 1) * size, size, cancellationToken);

        var total = await quotations.CountForCustomerAsync(customerId, cancellationToken);

        return GeneralResponse<PagedResult<QuotationDto>>.Success(new PagedResult<QuotationDto>
        {
            Items = [.. rows.Select(row => QuotationMapper.ToDto(row, today, forStaff: false))],
            Total = total,
            Page = current,
            PageSize = size
        });
    }

    // -------------------------------------------------------------------------
    // Writing
    // -------------------------------------------------------------------------

    public async Task<GeneralResponse<QuotationDto>> SaveAsync(
        long? id, SaveQuotationDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.Lines.Count == 0)
        {
            return Fail(QuotationErrors.NoLines, "A quotation needs something on it.");
        }

        if (dto.Lines.Count > QuotationRules.MaxLines)
        {
            return Fail(
                QuotationErrors.NoLines,
                $"A quotation can carry at most {QuotationRules.MaxLines} lines.");
        }

        if (!PhoneNumber.TryParse(dto.ContactPhone, out var phone) || phone is null)
        {
            return Fail(
                QuotationErrors.ContactPhoneInvalid,
                "That does not look like a Bangladeshi mobile number.");
        }

        var validUntil = dto.ValidUntil ?? clock.DhakaToday.AddDays(QuotationRules.DefaultValidDays);

        if (validUntil < clock.DhakaToday)
        {
            return Fail(
                QuotationErrors.ValidUntilInvalid,
                "A quotation cannot be good until a day that has already passed.");
        }

        DeliveryAddress? address = null;

        if (dto.ShippingAddress is not null)
        {
            try
            {
                address = ToAddress(dto.ShippingAddress);
            }
            catch (ArgumentException ex)
            {
                return Fail(QuotationErrors.AddressRequired, ex.Message);
            }
        }

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            // Everything that can be refused is decided before a quotation
            // exists to be refused for. A number drawn from the sequence and
            // then rolled back leaves a gap in WHQ-2609-…, and gaps are an
            // afternoon somebody spends trying to explain.
            var built = await BuildLinesAsync(dto, Money.Bdt, ct);

            if (!built.IsSuccess)
            {
                return Fail(built.ErrorCode!, built.Message);
            }

            var lines = built.Data!;
            var zone = address is null ? DeliveryZone.InsideDhaka : DeliveryZoneResolver.Resolve(address);

            var context = await pricing.BuildAsync(
                // No address yet means no delivery figure yet. Passing the zone
                // as null is how the pricer is told to leave it pending rather
                // than to charge nothing.
                address is null ? null : zone,
                dto.DeliveryFeeOverride is { } over ? Money.Taka(over) : null,
                ct);

            var discount = Money.Taka(dto.Discount);
            var goods = lines.Aggregate(
                Money.Zero(Money.Bdt), (running, line) => running + line.Priced.LineTotal);

            if (discount > goods)
            {
                return Fail(
                    QuotationErrors.DiscountTooLarge,
                    "The discount is larger than the goods it comes off.");
            }

            Quotation quotation;
            var creating = id is null;

            if (id is { } existingId)
            {
                var found = await quotations.GetDetailAsync(existingId, ct);

                if (found is null)
                {
                    return Fail(QuotationErrors.NotFound, "We could not find that quotation.");
                }

                if (!QuotationStatusMachine.IsEditable(found.Status))
                {
                    // Pulling it back to Draft is the way to change one the
                    // customer is holding, and that is a deliberate act.
                    return Fail(
                        QuotationErrors.NotEditable,
                        "Only a draft can be edited. Put it back to draft first.");
                }

                quotation = found;
                quotation.Lines.Clear();
            }
            else
            {
                quotation = new Quotation
                {
                    QuotationNumber = await numbers.NextAsync(
                        NumberSequenceService.Quotations,
                        GlobalConstants.DefaultQuotationNumberPrefix,
                        ct),
                    Status = QuotationStatus.Draft
                };

                await quotations.InsertAsync(quotation, ct);
            }

            var totals = CartPricer.Price(
                [.. lines.Select(line => line.Priced)],
                context with { Discount = discount });

            Apply(quotation, dto, phone, address, zone, validUntil, totals, context);

            foreach (var line in lines)
            {
                quotation.Lines.Add(line.Entity);
            }

            // Only when it already existed. A row still waiting for its
            // identity cannot be marked Modified — EF refuses, and rightly:
            // there is nothing yet to modify.
            if (!creating)
            {
                quotations.Update(quotation);
            }

            await unitOfWork.SaveChangesAsync(ct);

            QuotationLog.Saved(
                logger, quotation.QuotationNumber, quotation.Lines.Count, quotation.GrandTotal.Amount);

            var saved = await quotations.GetDetailAsync(quotation.Id, ct);

            return Ok(saved ?? quotation, forStaff: true);
        }, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Moving it along
    // -------------------------------------------------------------------------

    public async Task<GeneralResponse<QuotationDto>> SetStatusAsync(
        string quotationNumber,
        QuotationStatus status,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(status))
        {
            return Fail(
                QuotationErrors.TransitionInvalid, "That is not a status a quotation can be in.");
        }

        if (status == QuotationStatus.Converted)
        {
            // Converting is a write against the orders table, not a label
            // change, and it has its own endpoint for that reason.
            return Fail(
                QuotationErrors.TransitionInvalid,
                "A quotation becomes Converted by being turned into an order.");
        }

        var quotation = await quotations.GetByNumberAsync(quotationNumber, cancellationToken);

        if (quotation is null)
        {
            return Fail(QuotationErrors.NotFound, "We could not find that quotation.");
        }

        if (status == QuotationStatus.Sent)
        {
            var ready = ReadyToSend(quotation);

            if (ready is not null)
            {
                return ready;
            }
        }

        return await MoveAsync(quotation, status, reason, forStaff: true, cancellationToken);
    }

    public async Task<GeneralResponse<QuotationDto>> AcceptAsync(
        string quotationNumber, string? contactPhone, CancellationToken cancellationToken = default)
    {
        var quotation = await quotations.GetByNumberAsync(quotationNumber, cancellationToken);

        if (quotation is null || !MayView(quotation, contactPhone))
        {
            return Fail(QuotationErrors.NotFound, "We could not find that quotation.");
        }

        if (!QuotationStatusMachine.IsAnswerable(quotation.Status))
        {
            return Fail(
                QuotationErrors.NotAnswerable,
                "That quotation can no longer be answered here. Please telephone us.");
        }

        // Refused rather than honoured. Timber prices move, and a figure from
        // three months ago is one the shop may no longer be able to stand
        // behind — so it is re-quoted rather than quietly accepted.
        if (QuotationStatusMachine.HasLapsed(quotation.Status, quotation.ValidUntil, clock.DhakaToday))
        {
            return Fail(
                QuotationErrors.Expired,
                "That quotation has run out. Please telephone us and we will price it again.");
        }

        return await MoveAsync(
            quotation, QuotationStatus.Accepted, null, forStaff: false, cancellationToken);
    }

    public async Task<GeneralResponse<QuotationDto>> DeclineAsync(
        string quotationNumber,
        string? contactPhone,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var quotation = await quotations.GetByNumberAsync(quotationNumber, cancellationToken);

        if (quotation is null || !MayView(quotation, contactPhone))
        {
            return Fail(QuotationErrors.NotFound, "We could not find that quotation.");
        }

        if (!QuotationStatusMachine.IsAnswerable(quotation.Status))
        {
            return Fail(
                QuotationErrors.NotAnswerable, "That quotation can no longer be answered here.");
        }

        return await MoveAsync(
            quotation, QuotationStatus.Declined, reason, forStaff: false, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Becoming an order
    // -------------------------------------------------------------------------

    public async Task<GeneralResponse<PlacedOrderDto>> ConvertAsync(
        string quotationNumber,
        ConvertQuotationDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var quotation = await quotations.GetByNumberAsync(quotationNumber, cancellationToken);

        if (quotation is null)
        {
            return GeneralResponse<PlacedOrderDto>.Fail(
                QuotationErrors.NotFound, "We could not find that quotation.");
        }

        if (quotation.Status == QuotationStatus.Converted)
        {
            return GeneralResponse<PlacedOrderDto>.Fail(
                QuotationErrors.AlreadyConverted,
                $"That quotation is already order {quotation.ConvertedOrder?.OrderNumber}.");
        }

        if (quotation.Status != QuotationStatus.Accepted)
        {
            return GeneralResponse<PlacedOrderDto>.Fail(
                QuotationErrors.NotAccepted,
                "The customer has not accepted that quotation yet.");
        }

        if (quotation.ShippingAddress is null)
        {
            return GeneralResponse<PlacedOrderDto>.Fail(
                QuotationErrors.AddressRequired, "There is nowhere to deliver this to.");
        }

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var code = string.IsNullOrWhiteSpace(dto.PaymentMethodCode)
                ? PaymentMethodCodes.CashOnDelivery
                : dto.PaymentMethodCode.Trim();

            var method = await payments.ResolveAsync(code, quotation.GrandTotal, quotation.DeliveryZone, ct);

            if (method is not { } chosen)
            {
                return GeneralResponse<PlacedOrderDto>.Fail(
                    PaymentErrors.MethodNotAvailable,
                    "That payment method is not available for this order.");
            }

            var order = BuildOrder(quotation, chosen, dto.DeliveryNote);

            var prefix = await settings.GetStringAsync(SettingKeys.OrderNumberPrefix, ct);

            order.OrderNumber = await numbers.NextAsync(
                NumberSequenceService.Orders,
                string.IsNullOrWhiteSpace(prefix)
                    ? GlobalConstants.DefaultOrderNumberPrefix
                    : prefix.Trim(),
                ct);

            AddLines(order, quotation);

            order.Timeline.Add(new OrderTimelineEntry
            {
                FromStatus = null,
                ToStatus = OrderStatus.Pending,
                ActorUserId = currentUser.UserId,
                ActorName = currentUser.PhoneNumber ?? "Staff",
                Note = $"Placed from quotation {quotation.QuotationNumber}.",
                OccurredAt = clock.UtcNow
            });

            await orders.InsertAsync(order, ct);
            await unitOfWork.SaveChangesAsync(ct);

            // Only the lines that came off a shelf hold anything. A wardrobe
            // built to an alcove has no stock to reserve, and refusing the
            // order because of that would be refusing the sale the quotation
            // existed to make.
            var reserved = await inventory.ReserveForOrderAsync(order, ct);

            if (!reserved.IsSuccess)
            {
                return GeneralResponse<PlacedOrderDto>.Invalid(
                    reserved.ErrorCode ?? InventoryErrors.InsufficientStock,
                    reserved.Message,
                    reserved.Errors ?? new Dictionary<string, string[]>());
            }

            quotation.Status = QuotationStatus.Converted;
            quotation.ConvertedOrderId = order.Id;

            quotations.Update(quotation);

            await QueueAsync(quotation, "quotation.converted", order.OrderNumber, ct);
            await unitOfWork.SaveChangesAsync(ct);

            QuotationLog.Converted(
                logger, quotation.QuotationNumber, order.OrderNumber, order.GrandTotal.Amount);

            return GeneralResponse<PlacedOrderDto>.Success(
                new PlacedOrderDto
                {
                    Id = order.Id,
                    OrderNumber = order.OrderNumber,
                    Status = order.Status,
                    GrandTotal = order.GrandTotal.Amount
                },
                id: order.Id);
        }, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Building
    // -------------------------------------------------------------------------

    /// <summary>One line, as the entity to store and as the pricer sees it.</summary>
    private readonly record struct BuiltLine(QuotationLine Entity, PricedLine Priced);

    private async Task<GeneralResponse<List<BuiltLine>>> BuildLinesAsync(
        SaveQuotationDto dto, string currency, CancellationToken ct)
    {
        var built = new List<BuiltLine>();
        var order = 0;

        foreach (var line in dto.Lines)
        {
            if (line.Quantity <= 0)
            {
                return GeneralResponse<List<BuiltLine>>.Fail(
                    QuotationErrors.QuantityInvalid, "Every line needs a quantity of at least one.");
            }

            QuotationLine entity;
            Money? insideDhaka = null;
            Money? outsideDhaka = null;

            if (line.ProductVariantId is { } variantId)
            {
                var variant = await variants.GetWithProductAsync(variantId, ct);

                if (variant?.Product is null)
                {
                    return GeneralResponse<List<BuiltLine>>.Fail(
                        QuotationErrors.VariantNotFound, "One of those products no longer exists.");
                }

                var product = variant.Product;

                // The catalogue's price unless the designer typed one. Quoting
                // a bed at a negotiated figure is ordinary, and the quotation
                // is what the customer will be held to either way.
                var unit = line.UnitPrice is { } typed ? Money.Taka(typed) : variant.EffectivePrice;

                insideDhaka = product.DeliveryChargeInsideDhaka;
                outsideDhaka = product.DeliveryChargeOutsideDhaka;

                entity = new QuotationLine
                {
                    ProductVariantId = variant.Id,
                    ProductId = product.Id,
                    Description = string.IsNullOrWhiteSpace(line.Description)
                        ? product.Name.En
                        : line.Description.Trim(),
                    VariantName = variant.VariantName,
                    Sku = variant.Sku,
                    ImagePath = product.Media.FirstOrDefault(media => media.IsPrimary)?.StoragePath,
                    Quantity = line.Quantity,
                    UnitPrice = unit,
                    LineTotal = unit.Multiply(line.Quantity),
                    LeadTimeDays = line.LeadTimeDays ?? product.LeadTimeDays,
                    SortOrder = order++
                };
            }
            else
            {
                if (string.IsNullOrWhiteSpace(line.Description))
                {
                    return GeneralResponse<List<BuiltLine>>.Fail(
                        QuotationErrors.DescriptionRequired,
                        "A line that is not from the catalogue needs a description.");
                }

                if (line.UnitPrice is not { } price)
                {
                    return GeneralResponse<List<BuiltLine>>.Fail(
                        QuotationErrors.PriceInvalid,
                        "A line that is not from the catalogue needs a price.");
                }

                var unit = Money.Taka(price);

                entity = new QuotationLine
                {
                    Description = line.Description.Trim(),
                    Quantity = line.Quantity,
                    UnitPrice = unit,
                    LineTotal = unit.Multiply(line.Quantity),
                    LeadTimeDays = line.LeadTimeDays,
                    SortOrder = order++
                };
            }

            built.Add(new BuiltLine(
                entity,
                new PricedLine(entity.Quantity, entity.UnitPrice, insideDhaka, outsideDhaka)));
        }

        return GeneralResponse<List<BuiltLine>>.Success(built);
    }

    private void Apply(
        Quotation quotation,
        SaveQuotationDto dto,
        PhoneNumber phone,
        DeliveryAddress? address,
        DeliveryZone zone,
        DateOnly validUntil,
        CartTotals totals,
        PricingContext context)
    {
        quotation.BookingId = dto.BookingId;

        // CustomerId is deliberately not set here. A quotation is written by
        // staff, usually for somebody with no account, and the phone number is
        // what links it to one made later — the same rule as an order and a
        // booking.
        quotation.ContactName = dto.ContactName.Trim();
        quotation.ContactPhone = phone.Value;
        quotation.ContactEmail = string.IsNullOrWhiteSpace(dto.ContactEmail)
            ? null
            : dto.ContactEmail.Trim();

        quotation.ShippingAddress = address;
        quotation.DeliveryZone = zone;
        quotation.ValidUntil = validUntil;

        quotation.Subtotal = totals.Subtotal;
        quotation.DiscountTotal = totals.DiscountTotal;
        quotation.GoodsNet = totals.GoodsNet;
        quotation.VatAmount = totals.VatAmount;
        quotation.VatRatePercent = context.VatRatePercent;
        quotation.PricesIncludeVat = context.PricesIncludeVat;
        quotation.DeliveryFee = totals.DeliveryFee;
        quotation.DeliveryFeeOverride = context.DeliveryFeeOverride;
        quotation.GrandTotal = totals.GrandTotal;

        quotation.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
        quotation.InternalNotes = string.IsNullOrWhiteSpace(dto.InternalNotes)
            ? null
            : dto.InternalNotes.Trim();
    }

    /// <summary>
    /// Builds the order from the quotation's own figures.
    /// </summary>
    /// <remarks>
    /// Every number is copied, including the VAT rate and the inclusive flag.
    /// Re-reading them would mean a quotation given last month at 5% becoming
    /// an order at 7.5%, which is not what the customer agreed to. The payment
    /// surcharge is the one figure added, because it belongs to the way they
    /// chose to pay rather than to what they were quoted.
    /// </remarks>
    private Order BuildOrder(Quotation quotation, EligiblePaymentMethod method, string? deliveryNote) =>
        new()
        {
            CustomerId = quotation.CustomerId,
            ContactName = quotation.ContactName,
            ContactPhone = quotation.ContactPhone,
            ContactEmail = quotation.ContactEmail,
            CustomerLanguage = quotation.CustomerLanguage,
            ShippingAddress = quotation.ShippingAddress!,
            DeliveryZone = quotation.DeliveryZone,
            DeliveryNote = string.IsNullOrWhiteSpace(deliveryNote) ? null : deliveryNote.Trim(),

            Currency = quotation.Currency,
            Subtotal = quotation.Subtotal,
            DiscountTotal = quotation.DiscountTotal,
            GoodsNet = quotation.GoodsNet,
            VatAmount = quotation.VatAmount,
            VatRatePercent = quotation.VatRatePercent,
            PricesIncludeVat = quotation.PricesIncludeVat,
            DeliveryFee = quotation.DeliveryFee,
            PaymentSurcharge = method.Surcharge,
            GrandTotal = quotation.GrandTotal + method.Surcharge,
            DeliveryWaived = false,
            DeliveryOverridden = quotation.DeliveryFeeOverride is not null,

            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Unpaid,
            FulfilmentStatus = FulfilmentStatus.Unfulfilled,
            PaymentMethodCode = method.Config.Code,
            PlacedAt = clock.UtcNow,

            // The quotation number, so a double press of Convert finds the
            // order it already made rather than making a second one. The unique
            // index on the column is what settles the race the check cannot.
            IdempotencyKey = $"quotation:{quotation.QuotationNumber}"
        };

    private static void AddLines(Order order, Quotation quotation)
    {
        foreach (var line in quotation.Lines.OrderBy(line => line.SortOrder))
        {
            order.Lines.Add(new OrderLine
            {
                ProductVariantId = line.ProductVariantId,
                ProductId = line.ProductId,

                // The quotation's words, not the catalogue's. The customer
                // agreed to "wardrobe, 7ft, to the alcove", and that is what
                // the invoice has to say.
                ProductNameEn = line.Description,
                ProductSlug = string.Empty,
                Sku = line.Sku ?? string.Empty,
                VariantName = line.VariantName ?? string.Empty,
                ImagePath = line.ImagePath,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                DiscountAmount = Money.Zero(order.Currency),
                LineTotal = line.LineTotal,

                // Delivery was priced on the quotation as a whole and is
                // carried on the order as one figure; splitting it back over
                // the lines here would be inventing a number.
                DeliveryChargeApplied = Money.Zero(order.Currency),
                LeadTimeDays = line.LeadTimeDays
            });
        }
    }

    // -------------------------------------------------------------------------
    // Shared
    // -------------------------------------------------------------------------

    private async Task<GeneralResponse<QuotationDto>> MoveAsync(
        Quotation quotation,
        QuotationStatus to,
        string? reason,
        bool forStaff,
        CancellationToken cancellationToken)
    {
        if (!QuotationStatusMachine.CanTransition(quotation.Status, to))
        {
            return Fail(
                QuotationErrors.TransitionInvalid,
                $"A {quotation.Status} quotation cannot become {to}.");
        }

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var from = quotation.Status;
            var now = clock.UtcNow;

            quotation.Status = to;

            switch (to)
            {
                case QuotationStatus.Sent:
                    quotation.SentAt = now;

                    // Cleared, because this is a new offer: an old refusal
                    // sitting beside a fresh quotation reads as the customer
                    // having declined the one they are being shown.
                    quotation.RespondedAt = null;
                    quotation.DeclineReason = null;
                    break;

                case QuotationStatus.Accepted:
                    quotation.RespondedAt = now;
                    quotation.DeclineReason = null;
                    break;

                case QuotationStatus.Declined:
                    quotation.RespondedAt = now;
                    quotation.DeclineReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
                    break;
            }

            quotations.Update(quotation);

            if (to is QuotationStatus.Sent or QuotationStatus.Expired)
            {
                await QueueAsync(quotation, $"quotation.{to.ToString().ToLowerInvariant()}", null, ct);
            }

            await unitOfWork.SaveChangesAsync(ct);

            QuotationLog.Moved(logger, quotation.QuotationNumber, from, to);

            return Ok(quotation, forStaff);
        }, cancellationToken);
    }

    /// <summary>
    /// Whether this quotation is fit to send.
    /// </summary>
    /// <remarks>
    /// Checked here rather than at save time, because a designer building one
    /// up over an afternoon should not be stopped from saving a half-finished
    /// draft. Sending is the moment it has to be complete.
    /// </remarks>
    private GeneralResponse<QuotationDto>? ReadyToSend(Quotation quotation)
    {
        if (quotation.Lines.Count == 0)
        {
            return Fail(QuotationErrors.NoLines, "There is nothing on that quotation to send.");
        }

        if (quotation.ShippingAddress is null)
        {
            return Fail(
                QuotationErrors.AddressRequired,
                "Delivery cannot be priced without an address, so it cannot be sent without one.");
        }

        if (quotation.ValidUntil < clock.DhakaToday)
        {
            return Fail(
                QuotationErrors.ValidUntilInvalid,
                "That quotation is good until a day that has already passed.");
        }

        return null;
    }

    private async Task QueueAsync(
        Quotation quotation, string type, string? orderNumber, CancellationToken ct) =>
        await notifications.EnqueueAsync(
            new NotificationRequest
            {
                Type = type,

                // Keyed on the moment, so a quotation revised and sent again
                // is a second message rather than one the outbox swallows as a
                // duplicate of the first.
                IdempotencyKey = $"{type}:{quotation.QuotationNumber}:{clock.UtcNow:O}",
                Payload = JsonSerializer.Serialize(new
                {
                    quotationNumber = quotation.QuotationNumber,
                    contactName = quotation.ContactName,
                    contactPhone = quotation.ContactPhone,
                    contactEmail = quotation.ContactEmail,
                    language = quotation.CustomerLanguage,
                    grandTotal = quotation.GrandTotal.Amount,
                    currency = quotation.Currency,
                    validUntil = quotation.ValidUntil.ToString("d MMMM", System.Globalization.CultureInfo.InvariantCulture),
                    lineCount = quotation.Lines.Count,
                    orderNumber
                })
            },
            ct);

    /// <summary>
    /// Whether this caller may see this quotation.
    /// </summary>
    /// <remarks>
    /// A signed-in customer sees their own. A guest quotes the number and the
    /// phone it was written for — the same two facts an order and a booking are
    /// found by, and the reason a guessed number on its own discloses nothing.
    /// </remarks>
    private bool MayView(Quotation quotation, string? contactPhone)
    {
        if (currentUser.UserId is { } userId && quotation.CustomerId == userId)
        {
            return true;
        }

        return PhoneNumber.TryParse(contactPhone, out var parsed)
               && parsed is not null
               && string.Equals(parsed.Value, quotation.ContactPhone, StringComparison.Ordinal);
    }

    private GeneralResponse<QuotationDto> Ok(Quotation quotation, bool forStaff) =>
        GeneralResponse<QuotationDto>.Success(
            QuotationMapper.ToDto(quotation, clock.DhakaToday, forStaff), id: quotation.Id);

    private static DeliveryAddress ToAddress(DeliveryAddressDto dto) =>
        DeliveryAddress.Create(
            dto.Division,
            dto.District,
            dto.AddressLine,
            dto.Upazila,
            dto.Area,
            dto.Landmark,
            dto.Postcode);

    private static GeneralResponse<QuotationDto> Fail(string code, string message) =>
        GeneralResponse<QuotationDto>.Fail(code, message);
}

/// <summary>
/// Structured logging for quotations.
/// </summary>
/// <remarks>Event ids 2300–2302.</remarks>
internal static partial class QuotationLog
{
    [LoggerMessage(
        EventId = 2300,
        Level = LogLevel.Information,
        Message = "Quotation {QuotationNumber} saved: {Lines} line(s), {Total}.")]
    public static partial void Saved(
        ILogger logger, string quotationNumber, int lines, decimal total);

    [LoggerMessage(
        EventId = 2301,
        Level = LogLevel.Information,
        Message = "Quotation {QuotationNumber} moved from {From} to {To}.")]
    public static partial void Moved(
        ILogger logger, string quotationNumber, QuotationStatus from, QuotationStatus to);

    [LoggerMessage(
        EventId = 2302,
        Level = LogLevel.Information,
        Message = "Quotation {QuotationNumber} became order {OrderNumber} at {Total}.")]
    public static partial void Converted(
        ILogger logger, string quotationNumber, string orderNumber, decimal total);
}
