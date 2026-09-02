using Money.Infrastructure.Persistence;

namespace Money.Application.Tests.Persistence;

public sealed class DataDirectoryTests
{
    [Fact]
    public void Without_an_override_the_data_directory_is_under_the_roaming_app_data_root()
    {
        DataDirectory.Resolve(overrideDirectory: null, appDataRoot: @"C:\Users\jt\AppData\Roaming")
            .Should().Be(Path.Combine(@"C:\Users\jt\AppData\Roaming", "MoneyApp"));
    }

    [Fact]
    public void An_override_wins()
    {
        DataDirectory.Resolve(@"D:\money-data", @"C:\Users\jt\AppData\Roaming")
            .Should().Be(@"D:\money-data");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_override_is_ignored(string overrideDirectory)
    {
        DataDirectory.Resolve(overrideDirectory, @"C:\Users\jt\AppData\Roaming")
            .Should().EndWith("MoneyApp");
    }

    [Fact]
    public void The_database_and_backups_live_inside_the_data_directory()
    {
        DataDirectory.DatabasePathIn(@"D:\money-data")
            .Should().Be(Path.Combine(@"D:\money-data", "money.db"));
        DataDirectory.BackupDirectoryIn(@"D:\money-data")
            .Should().Be(Path.Combine(@"D:\money-data", "backups"));
    }

    [Fact]
    public void The_connection_string_enables_foreign_keys_and_names_the_file()
    {
        var connectionString = DataDirectory.ConnectionStringFor(@"D:\money-data\money.db");

        connectionString.Should().Contain(@"D:\money-data\money.db");
        connectionString.Should().Contain("Foreign Keys=True");
    }
}
