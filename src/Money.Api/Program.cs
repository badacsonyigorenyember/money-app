using Money.Api;

var app = await MoneyWebApp.CreateAsync(new WebApplicationOptions { Args = args });

await app.RunAsync();

/// <summary>Exposed so WebApplicationFactory&lt;Program&gt; can host the app in tests.</summary>
public partial class Program;
