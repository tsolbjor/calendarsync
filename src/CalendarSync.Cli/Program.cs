using System.CommandLine;
using CalendarSync.Cli.Commands;
using CalendarSync.Cli.Providers;

var registry = new ProviderRegistry([
    new MicrosoftCalendarProvider(),
    new GoogleCalendarProvider(),
    new GooglePersonalCalendarProvider(),
    new ReadOnlyCalendarProvider()
]);

var rootCmd = new RootCommand("CalendarSync - Plan your week across multiple calendar providers");

rootCmd.Add(AccountCommands.Build(registry));
rootCmd.Add(SetupCommand.Build(registry));

rootCmd.Add(PlanCommand.Build(registry));
rootCmd.Add(ValidateCommand.Build(registry));

var parseResult = rootCmd.Parse(args);
return await parseResult.InvokeAsync();
