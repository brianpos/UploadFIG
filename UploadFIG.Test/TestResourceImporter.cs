extern alias r4b;

using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Specification.Source;
using UnitTestWebApi;

namespace UploadFIG.Test
{
    [TestClass]
    public class TestResourceImporter
    {
        [TestMethod]
        public async Task TestIgnoreDuplicateCanonicals()
        {
            var app = new UnitTestFhirServerApplication();
            Program.useClient = app.CreateClient();

            string outputFile = "c:\\temp\\uploadfig-dump-au-smartforms.json";
            var args = new[]
            {
                // "-t",
                "-vq",
                "-r", "Questionnaire",
                "-r", "ValueSet",
                "-r", "CodeSystem",
                "-s", "https://build.fhir.org/ig/aehrc/smart-forms-ig/branches/master/package.tgz",
                "-d", "https://localhost",
                "-odf", outputFile,
                // "-sf", "package/SearchParameter-valueset-extensions-ValueSet-end.json",
                // "--verbose",
            };
            var result = await Program.Main(args);

            string tempFIGpath = Path.Combine(Path.GetTempPath(), "UploadFIG");
            string unitTestPath = Path.Combine(tempFIGpath, "unit-test-data");
            int loadedResourceCount = Directory.EnumerateFiles(unitTestPath).Count();

            // run it again to ensure that we don't get any new versions of resources
            result = await Program.Main(args);
            Assert.AreEqual(loadedResourceCount, Directory.EnumerateFiles(unitTestPath).Count());
            Assert.AreEqual(0, result);

            string json = System.IO.File.ReadAllText(outputFile);
            var output = System.Text.Json.JsonSerializer.Deserialize<OutputDependenciesFile>(json);
            Bundle bun = new Bundle();

            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("Recursively Scanning Dependencies...");
            PrepareDependantPackage.RecursivelyScanPackageForCanonicals(output, bun);

            //var coreSource = new CachedResolver(ZipSource.CreateValidationSource());
            //var r = coreSource.ResolveByCanonicalUri("http://hl7.org/fhir/ValueSet/care-plan-intent");
            //Assert.IsNotNull(r);
        }

        [TestMethod]
        public async Task TestIgnoreDuplicateCanonicalsPermit()
        {
            var app = new UnitTestFhirServerApplication();
            Program.useClient = app.CreateClient();

            string outputFile = "c:\\temp\\uploadfig-dump-au-smartforms.json";
            var args = new[]
            {
                // "-t",
                "-vq",
                "-r", "Questionnaire",
                "-r", "ValueSet",
                "-r", "CodeSystem",
                "-s", "https://build.fhir.org/ig/aehrc/smart-forms-ig/branches/master/package.tgz",
                "-d", "https://localhost",
                "-odf", outputFile,
                "-pdv", "false",
                // "-sf", "package/SearchParameter-valueset-extensions-ValueSet-end.json",
                // "--verbose",
            };
            var result = await Program.Main(args);

            string tempFIGpath = Path.Combine(Path.GetTempPath(), "UploadFIG");
            string unitTestPath = Path.Combine(tempFIGpath, "unit-test-data");
            int loadedResourceCount = Directory.EnumerateFiles(unitTestPath).Count();

            // run it again to ensure that we don't get any new versions of resources
            result = await Program.Main(args);
            Assert.AreEqual(loadedResourceCount, Directory.EnumerateFiles(unitTestPath).Count());
            Assert.AreEqual(0, result);

            string json = System.IO.File.ReadAllText(outputFile);
            var output = System.Text.Json.JsonSerializer.Deserialize<OutputDependenciesFile>(json);
            Bundle bun = new Bundle();

            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("Recursively Scanning Dependencies...");
            PrepareDependantPackage.RecursivelyScanPackageForCanonicals(output, bun);
        }
    }
}