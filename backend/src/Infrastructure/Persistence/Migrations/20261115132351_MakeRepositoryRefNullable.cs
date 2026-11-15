using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeRepositoryRefNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ref",
                table: "repositories",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rows imported without a ref have NULL; the old schema required a value (it used to default to "main").
            migrationBuilder.Sql("UPDATE repositories SET ref = 'main' WHERE ref IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "ref",
                table: "repositories",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);
        }
    }
}
