using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WoodHeart.Repository.Migrations
{
    /// <summary>
    /// Makes <c>products.search_text</c> searchable by substring without a
    /// table scan, and makes "did you mean" possible at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The catalogue is searched with <c>ILIKE '%word%'</c>, which no ordinary
    /// B-tree index can serve — a leading wildcard has no prefix to seek on. A
    /// GIN index over trigrams can: it indexes every three-character run in the
    /// column, so a pattern is answered by intersecting the runs it contains.
    /// It works with a parameterised pattern, which a literal-free query plan
    /// needs, and it works for Bangla, whose characters are just characters to
    /// it.
    /// </para>
    /// <para>
    /// The same extension supplies <c>word_similarity()</c>, which is what
    /// turns a search that found nothing into a suggestion rather than an empty
    /// page — it scores the best-matching run inside a long text, where plain
    /// <c>similarity()</c> would score a one-word query against a hundred-word
    /// description as almost zero.
    /// </para>
    /// <para>
    /// <c>pg_trgm</c> is a trusted extension from PostgreSQL 13, so the owner
    /// of the database can install it without being a superuser. That matters:
    /// it means this migration runs as the application's own role in
    /// production, with no out-of-band step a deploy can forget.
    /// </para>
    /// </remarks>
    public partial class AddProductSearchTrigramIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS ix_products_search_text_trgm
                    ON products USING gin (search_text gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_products_search_text_trgm;");

            // The extension is deliberately left in place. Dropping it would
            // take every other trigram index in the database with it, and it
            // costs nothing to keep.
        }
    }
}
