using System.Runtime.CompilerServices;
using Argon;

namespace MongoFlow.IntegrationTests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        // Verify writes every snapshot under snapshots/ in this project, not next to the test file.
        UseProjectRelativeDirectory("snapshots");

        VerifierSettings.AddExtraSettings(settings =>
        {
            // A zero count, false or the first enum value is often what a test checks, so snapshots keep them.
            settings.DefaultValueHandling = DefaultValueHandling.Include;
            settings.NullValueHandling = NullValueHandling.Ignore;
            settings.Converters.Add(new BsonValueConverter());
        });
    }
}
