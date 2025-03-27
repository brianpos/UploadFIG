extern alias r4b;

using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Utility;
using UnitTestWebApi;

namespace UploadFIG.Test
{
    [TestClass]
    public class TestResourceImporter
    {
        [TestMethod]
        public void TestSemanticVersionRange()
        {
            // Ensure the 3rd party assembly is working as expected
            var range = new SemanticVersioning.Range("3.8.x", true);
            List<SemanticVersioning.Version> versions = new List<SemanticVersioning.Version>();
            versions.Add(new SemanticVersioning.Version("3.8.1", true));
            versions.Add(new SemanticVersioning.Version("3.8.2-master", true));
            versions.Add(new SemanticVersioning.Version("3.8.2", true));
            versions.Add(new SemanticVersioning.Version("3.8.3", true));
            var results = versions.Order().ToList();

            Assert.AreEqual(4, results.Count());
            Assert.AreEqual("3.8.1", results[0].ToString());
            Assert.AreEqual("3.8.2-master", results[1].ToString());
            Assert.AreEqual("3.8.2", results[2].ToString());
            Assert.AreEqual("3.8.3", results[3].ToString());
        }

        [TestMethod]
        public async Task TestDeployUkCore()
        {
            var app = new UnitTestFhirServerApplication();
            Program.useClient = app.CreateClient();

            var args = new[]
            {
                "-vq",
                "-vrd",
                "--includeReferencedDependencies",
                "-pid", "fhir.r4.ukcore.stu3.currentbuild",
                "-pv", "0.0.8-pre-release",
                "-gs", "true",
                "-d", "https://localhost",
                // "--verbose",
            };
            var settings = CheckImplementationGuides.ParseArguments(args);
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

            CheckDeployResults(result);
        }

        [TestMethod]
        public async Task TestDeployFolder()
        {
            var app = new UnitTestFhirServerApplication();
            Program.useClient = app.CreateClient();

            var settings = CheckImplementationGuides.ParseArguments(new[]
            {
                "-s", @"C:\git\hl7\sdc\input\resources\*.json",
                "-cn",
                "-pcv",
                "-sn",
                "-fv", "R4",
                "-ap", "hl7.fhir.uv.sdc|4.0.0-ballot",
                "--includeReferencedDependencies",
                "--validateReferencedDependencies",
                "-d", "https://localhost",
                "-of", @"c:\temp\uploadfig-demo.json",
            });
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

            CheckDeployResults(result);
        }

        [TestMethod]
        public async Task TestDeployAuBase()
        {
            var app = new UnitTestFhirServerApplication();
            Program.useClient = app.CreateClient();

            var args = new[]
            {
                "-vq",
                "--includeExamples",
                "-pid", "hl7.fhir.au.base",
                "-pv", "4.1.0",
                "-cn",
                "-pdv", "false", // allow duplicate versions
                "-pcv",
                "-sn",
                "--includeReferencedDependencies",
                "--validateReferencedDependencies",
                "-d", "https://localhost",
                "-reg", "https://api.healthterminologies.gov.au/integration/R4/fhir",
                // "--verbose"
                "-of", @"c:\temp\uploadfig-dump-aubase_410-bundle.json",
            };
            var settings = CheckImplementationGuides.ParseArguments(args);
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

            CheckDeployResults(result);
        }

        private static void CheckDeployResults(Program.Result result)
        {
            string tempFIGPath = Path.Combine(Path.GetTempPath(), "UploadFIG");
            string unitTestPath = Path.Combine(tempFIGPath, "unit-test-data");
            var resourceTypesLoaded = result.AlternativeOutputBundle?.Entry
                .Select(e => e.Resource?.TypeName)
                .Distinct()
                .ToList();
            foreach (var typeName in resourceTypesLoaded)
            {
                int typeResourceCountLoaded = Directory.EnumerateFiles(unitTestPath, $"{typeName}.*..xml", new EnumerationOptions() { MatchCasing = MatchCasing.CaseInsensitive }).Count();
                int typesInBundleCount = result.AlternativeOutputBundle.Entry
                    .Where(e => e.Resource?.TypeName == typeName)
                    .Count();
                if (typeResourceCountLoaded != typesInBundleCount)
                {
                    // Check out what's missing
                    var server = new Hl7.Fhir.Rest.BaseFhirClient(new Uri("https://localhost"), Program.useClient, result.Processor.ModelInspector);
                    var loaded = server.Search(typeName, [], pageSize: 400);
                    var expectedResources = result.AlternativeOutputBundle.Entry
                        .Where(e => e.Resource?.TypeName == typeName)
                        .Select(e => e.Resource)
                        .ToList();
                    var loadedResources = loaded.Entry
                        .Select(e => e.Resource)
                        .ToList();
                    foreach (var loadededResource in loadedResources.ToArray())
                    {
                        if (loadededResource is IVersionableConformanceResource ivrLoaded)
                        {
                            var expectedResource = expectedResources.OfType<IVersionableConformanceResource>().Where(e => e.Url == ivrLoaded.Url && e.Version == ivrLoaded.Version).ToList();
                            if (expectedResource?.Count == 0)
                            {
                                // This resource is not in the bundle
                                // Check the type
                                Console.Error.WriteLine($"Missing {typeName} resource: {ivrLoaded.Url}|{ivrLoaded.Version}");
                            }
                            if (expectedResource?.Count > 1)
                            {
                                // There are multiple of this resource in there.
                                Console.Error.WriteLine($"Multiple {typeName} resource: {ivrLoaded.Url} | {ivrLoaded.Version} - {expectedResource.Count}");
                            }
                            expectedResources.RemoveAll(r => expectedResource.Contains(r as IVersionableConformanceResource));
                            loadedResources.Remove(loadededResource);
                        }
                    }

                    // Now check the reverse
                    foreach (var expectedResource in expectedResources)
                    {
                        if (expectedResource is IVersionableConformanceResource ivrExpected)
                        {
                            var loadedResource = loadedResources.OfType<IVersionableConformanceResource>().Where(e => e.Url == ivrExpected.Url && e.Version == ivrExpected.Version).ToList();
                            if (loadedResource?.Count == 0)
                            {
                                // This resource is not in the bundle
                                // Check the type
                                Console.Error.WriteLine($"Missing {typeName} resource: {ivrExpected.Url}|{ivrExpected.Version}");
                            }
                            if (loadedResource?.Count > 1)
                            {
                                // There are multiple of this resource in there.
                                Console.Error.WriteLine($"Multiple {typeName} resource: {ivrExpected.Url} | {ivrExpected.Version} - {loadedResource.Count}");
                            }
                        }
                    }

                    // Check that all these remaining resources have the expect
                    Assert.AreEqual(0, expectedResources.Count(er => !er.HasAnnotation<FhirOperationException>()), $"{typeName} count miss-match");
                }
            }
        }

        [TestMethod]
        public async Task TestDeployAuCore()
        {
            var app = new UnitTestFhirServerApplication();
            Program.useClient = app.CreateClient();

            var args = new[]
            {
                "-vq",
                "-pid", "hl7.fhir.au.core",
                "-pv", "1.0.0",
                "-cn",				// clean narrative for MS server
                "-pdv", "false",	// allow duplicate versions
                // "-pcv",				// patch canonical versions
                "-sn",				// skip narrative
                // "--includeExamples",
                "--includeReferencedDependencies",
                "--validateReferencedDependencies",
                "-d", "http://localhost",
                "-reg", "https://api.healthterminologies.gov.au/integration/R4/fhir",
                // "--verbose"
                "-of", @"c:\temp\uploadfig-dump-aubase_410-bundle.json",
            };
            var settings = CheckImplementationGuides.ParseArguments(args);
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

            CheckDeployResults(result);
        }

        [TestMethod]
        public async Task TestDeployUsCoreCI()
        {
            var app = new UnitTestFhirServerApplication();
            Program.useClient = app.CreateClient();

            var args = new[]
            {
                "-s", "https://build.fhir.org/ig/HL7/US-Core/package.tgz",
                "-pid", "hl7.fhir.us.core",
                "-pv", "6.1.0",

                "-d", "https://localhost",

                "-pdv", "false",	// allow duplicate versions
                "-pcv",				// pin/patch canonical versions
                "-sn",				// strip narratives
                "--includeReferencedDependencies",
                "--validateReferencedDependencies",
                "-vq",
                // "--verbose",
                // "-r", "CapabilityStatement",
                // "-sf", "package/CapabilityStatement-us-core-server.json",
            };
            var settings = CheckImplementationGuides.ParseArguments(args);
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

            CheckDeployResults(result);
        }

        [TestMethod]
        public async Task TestDeployDavinci_crd()
        {
            var app = new UnitTestFhirServerApplication();
            Program.useClient = app.CreateClient();
            var args = new[]
            {
                "-vq",
                //"-r", "*",
                "-pdv", "false",	// allow duplicate versions
                "-pcv",				// pin/patch canonical versions
                "-sn",				// strip narratives
                "--validateReferencedDependencies",
                "--includeReferencedDependencies",
                "-pid", "hl7.fhir.us.davinci-crd",
                "-d", "https://localhost",
                // "--verbose",
                "-of", @"c:\temp\uploadfig-dump-davinci-crd-bundle.json",
            };
            var settings = CheckImplementationGuides.ParseArguments(args);
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

            CheckDeployResults(result);
        }

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
            var settings = CheckImplementationGuides.ParseArguments(args);
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

            Assert.AreEqual(1, result.failures);
            Assert.AreEqual(0, result.validationErrors);

            string tempFIGpath = Path.Combine(Path.GetTempPath(), "UploadFIG");
            string unitTestPath = Path.Combine(tempFIGpath, "unit-test-data");
            int loadedResourceCount = Directory.EnumerateFiles(unitTestPath).Count();

            // run it again to ensure that we don't get any new versions of resources
            result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(loadedResourceCount, Directory.EnumerateFiles(unitTestPath).Count());
            Assert.AreEqual(0, result.Value);
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
                "-vrd",
                "--includeReferencedDependencies",
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
        }
    }
}
