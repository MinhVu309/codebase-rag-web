using Infrastructure.Persistence;
using Npgsql;

namespace Api.Tests;

public class ConnectionStringResolverTests
{
    [Fact]
    public void Passes_Through_KeyValue_Connection_Strings()
    {
        const string cs = "Host=db;Port=5432;Database=x;Username=u;Password=p";
        Assert.Equal(cs, ConnectionStringResolver.Resolve(cs));
    }

    [Theory]
    [InlineData("postgres://user:p%40ss@db.example.com:6543/mydb")]
    [InlineData("postgresql://user:p%40ss@db.example.com:6543/mydb")]
    public void Converts_Postgres_Urls(string url)
    {
        var b = new NpgsqlConnectionStringBuilder(ConnectionStringResolver.Resolve(url));

        Assert.Equal("db.example.com", b.Host);
        Assert.Equal(6543, b.Port);
        Assert.Equal("mydb", b.Database);
        Assert.Equal("user", b.Username);
        Assert.Equal("p@ss", b.Password);
    }

    [Fact]
    public void Throws_When_Missing()
    {
        Assert.Throws<InvalidOperationException>(() => ConnectionStringResolver.Resolve(""));
    }
}
