using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Entity.Quotations;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Enums.Quotations;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Quotations;
using WoodHeart.Repository.Repositories.Quotations;
using WoodHeart.Service.Mapping.Quotations;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// The quotation list queries, against PostgreSQL.
/// </summary>
/// <remarks>
/// The same class of bug the order board had, found the same way — in a
/// browser, on a real row. The board prints the order a quotation became, and
/// that number lives on a related entity the search query did not load, so
/// every converted quotation on the board read as though it had become
/// nothing. A unit test with a substituted repository hands the mapper a
/// quotation whose order is already attached in memory, and passes.
/// </remarks>
public class QuotationRepositoryDatabaseTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>
{
    [RequiresPostgresFact]
    public async Task The_board_query_loads_the_order_a_quotation_became()
    {
        await using var context = fixture.CreateContext();

        var order = await SeedOrderAsync(context);
        var quotation = await SeedQuotationAsync(context, order, customerId: null);

        // A fresh context, so nothing is still tracked from the seeding and
        // the order can only come from the query itself.
        await using var reading = fixture.CreateContext();
        IQuotationRepository repository = new QuotationRepository(reading);

        // Narrowed to this one: the fixture's database is shared by the class,
        // and an unfiltered board would also carry the other test's row.
        var rows = await repository.SearchAsync(new QuotationSearch(
            Term: quotation.QuotationNumber, Status: null, BookingId: null, Page: 1, PageSize: 20));

        var found = rows.ShouldHaveSingleItem();

        QuotationMapper.ToListItem(found, DateOnly.FromDateTime(DateTime.UtcNow))
            .OrderNumber.ShouldBe(order.OrderNumber);
    }

    [RequiresPostgresFact]
    public async Task A_customers_own_list_says_which_order_theirs_became()
    {
        await using var context = fixture.CreateContext();

        var customer = await SeedCustomerAsync(context);
        var order = await SeedOrderAsync(context);
        await SeedQuotationAsync(context, order, customer);

        await using var reading = fixture.CreateContext();
        IQuotationRepository repository = new QuotationRepository(reading);

        var rows = await repository.GetForCustomerAsync(customer, skip: 0, take: 20);

        var found = rows.ShouldHaveSingleItem();

        QuotationMapper.ToDto(found, DateOnly.FromDateTime(DateTime.UtcNow), forStaff: false)
            .OrderNumber.ShouldBe(order.OrderNumber);
    }

    private static async Task<long> SeedCustomerAsync(DataContext context)
    {
        var user = new Domain.Entity.Identity.AppUser
        {
            UserName = $"0171{Random.Shared.Next(1_000_000, 9_999_999)}",
            PhoneNumber = $"+88017{Random.Shared.Next(10_000_000, 99_999_999)}",
            FullName = "Ayesha Rahman"
        };

        await context.Users.AddAsync(user);
        await context.SaveChangesAsync();

        return user.Id;
    }

    private static async Task<Order> SeedOrderAsync(DataContext context)
    {
        var order = new Order
        {
            OrderNumber = $"WH-Q{Guid.NewGuid():n}"[..16],
            ContactName = "Ayesha Rahman",
            ContactPhone = "+8801712345678",
            ShippingAddress = DeliveryAddress.Create(
                "Dhaka", "Dhaka", "House 27, Road 8", area: "Dhanmondi"),
            DeliveryZone = DeliveryZone.InsideDhaka,
            Currency = Money.Bdt,
            Subtotal = Money.Taka(185_000m),
            DiscountTotal = Money.Zero(),
            GoodsNet = Money.Taka(172_093m),
            VatAmount = Money.Taka(12_907m),
            VatRatePercent = 7.5m,
            PricesIncludeVat = true,
            DeliveryFee = Money.Zero(),
            PaymentSurcharge = Money.Zero(),
            GrandTotal = Money.Taka(185_000m),
            PaymentMethodCode = PaymentMethodCodes.CashOnDelivery,
            PlacedAt = DateTimeOffset.UtcNow
        };

        await context.Orders.AddAsync(order);
        await context.SaveChangesAsync();

        return order;
    }

    private static async Task<Quotation> SeedQuotationAsync(
        DataContext context, Order order, long? customerId)
    {
        var quotation = new Quotation
        {
            QuotationNumber = $"WHQ-Q{Guid.NewGuid():n}"[..16],
            CustomerId = customerId,
            ContactName = "Ayesha Rahman",
            ContactPhone = "+8801712345678",
            Currency = Money.Bdt,
            Subtotal = Money.Taka(185_000m),
            DiscountTotal = Money.Zero(),
            GoodsNet = Money.Taka(172_093m),
            VatAmount = Money.Taka(12_907m),
            VatRatePercent = 7.5m,
            PricesIncludeVat = true,
            DeliveryFee = Money.Zero(),
            GrandTotal = Money.Taka(185_000m),
            Status = QuotationStatus.Converted,
            ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14),
            ConvertedOrderId = order.Id,
            Lines =
            [
                new QuotationLine
                {
                    // The line the whole feature exists for: no variant behind
                    // it, its own words, and nothing to come off a shelf.
                    Description = "Wardrobe built to the alcove, 7ft, segun",
                    Quantity = 1,
                    UnitPrice = Money.Taka(185_000m),
                    LineTotal = Money.Taka(185_000m)
                }
            ]
        };

        await context.Quotations.AddAsync(quotation);
        await context.SaveChangesAsync();

        return quotation;
    }
}
