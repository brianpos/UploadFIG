extern alias r4b;

using Firely.Fhir.Packages;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Utility;
using r4b.Hl7.Fhir.Rest;
using System.Formats.Tar;

namespace UploadFIG.Test
{
    [TestClass]
    public class PrepareDependantPackage
    {
        [TestMethod]
        public async Task TestCommandLineUsage()
        {
            var result = await Program.Main(new string[]{ });
            Assert.AreEqual(1, result);
        }

        [TestMethod]
        public void CheckCapabilityStatement()
        {
            var nctsServer = new FhirClient("https://api.healthterminologies.gov.au/integration/R4/fhir");
            var r = nctsServer.CapabilityStatement();
            Assert.IsNotNull(r);
        }

        [TestMethod]
        public void DownloadPackage()
        {
        }

        [TestMethod]
        public void EnumerateDependantPackages()
        {

        }

        [TestMethod]
        public void CalculateDependantResources()
        {
        }

        [TestMethod]
        public void ScanPackageForCanonicals()
        {
            string json = System.IO.File.ReadAllText(@"C:\temp\uploadfig-dump-uscore.json");
            var output = System.Text.Json.JsonSerializer.Deserialize<OutputDependenciesFile>(json);
            Bundle bun = new Bundle();

            RecursivelyScanPackageForCanonicals(output, bun);

            // Finally refer out to the Registry defined to retrieve the contents
            var nctsServer = new FhirClient("https://api.healthterminologies.gov.au/integration/R4/fhir");
            //foreach (var dc in output.externalCanonicalsRequired)
            //{
            //    var r = nctsServer.Search(dc.resourceType, new[] { $"url={dc.canonical}" });
            //    if (r.Entry.Count > 1)
            //    {
            //        // Check if these are just more versions of the same thing, then to the canonical versioning thingy
            //        // to select the latest version.
            //        var cv = Hl7.Fhir.WebApi.CurrentCanonical.Current(r.Entry.Select(e => e.Resource as IVersionableConformanceResource));

            //        // remove all the others that aren't current.
            //        r.Entry.RemoveAll(e => e.Resource != cv as Resource);
            //    }
            //    bun.Entry.AddRange(r.Entry);
            //    Assert.IsNotNull(r);
            //}
            System.IO.File.WriteAllText(@"c:\temp\uploadfig-dependencies.json", new r4b.Hl7.Fhir.Serialization.FhirJsonSerializer(new SerializerSettings() { Pretty = true }).SerializeToString(bun));
        }

        public static void RecursivelyScanPackageForCanonicals(OutputDependenciesFile? output, Bundle bun)
        {
            // Prepare our own cache of fhir packages in this projects AppData folder
            var cache = new TempPackageCache();
            string cacheFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UploadFIG", "PackageCache");
            if (!Directory.Exists(cacheFolder))
                Directory.CreateDirectory(cacheFolder);
            Queue<KeyValuePair<string, string>> depPackages = new();
            foreach (var dp in output.dependencies)
            {
                depPackages.Enqueue(dp);
                // Console.WriteLine($"Added {dp.Key}|{dp.Value} for processing");
            }
            while (depPackages.Count > 0)
            {
                var dp = depPackages.Dequeue();

                Stream packageStream = cache.GetPackageStream(dp.Key, dp.Value);

                if (packageStream == null)
                {
                    // No package, so just need to continue
                    continue;
                }

                PackageManifest? manifest;
                using (packageStream)
                {
                    manifest = TempPackageCache.ReadManifest(packageStream);
                    if (manifest == null)
                        continue; // can't process the package without a manifest
                    PackageIndex? index = TempPackageCache.ReadPackageIndex(packageStream);

                    // Scan this package to see if any content is in the index
                    if (index != null)
                    {
                        System.Diagnostics.Trace.WriteLine($"Scanning index in {manifest.Name}");
                        foreach (var ecr in output.externalCanonicalsRequired)
                        {
                            var files = index.Files.Where(f => f.url == ecr.canonical);
                            if (files.Any())
                            {
                                System.Diagnostics.Trace.WriteLine($"   => found {ecr.canonical}|{ecr.version} in {String.Join(",", files.Select(f => f.version))}");
                                ecr.isMissing = false;
                                ecr.version = files.First().version;
                                ecr.foundInPackage = manifest.Name + "|" + manifest.Version;

                                // Read this file from the package
                                var content = TempPackageCache.ReadResourceContent(packageStream, files.First().filename);
                                if (content != null)
                                {
                                    Resource resource;
                                    if (files.First().filename.EndsWith(".json"))
                                    {
                                        resource = new r4b.Hl7.Fhir.Serialization.FhirJsonParser().Parse<Resource>(content);
                                    }
                                    else
                                    {
                                        resource = new r4b.Hl7.Fhir.Serialization.FhirXmlParser().Parse<Resource>(content);
                                    }
                                    if (resource is IVersionableConformanceResource ivr)
                                    {
                                        ecr.status = ivr.Status?.GetLiteral();

                                        // Lets see if there are any dependencies for this canonical resource
                                    }
                                    bun.AddResourceEntry(resource, $"{resource.TypeName}/{resource.Id}");
                                }
                            }
                        }
                    }
                }

                // Scan through this packages dependencies and see if I need to add more to the queue for processing
                if (manifest.Dependencies != null)
                {
                    foreach (var dep in manifest.Dependencies)
                    {
                        if (!output.dependencies.ContainsKey(dep.Key))
                        {
                            depPackages.Enqueue(dep);
                            output.dependencies.Add(dep.Key, dep.Value);
                            // Console.WriteLine($"Added {dep.Key}|{dep.Value} for processing");
                        }
                        else
                        {
                            if (dep.Value != output.dependencies[dep.Key])
                                Console.WriteLine($"      {manifest.Name}|{manifest.Version} => {dep.Key}|{dep.Value} is already processed with version {output.dependencies[dep.Key]}");
                        }
                    }
                }
            }

            // Remove all the items that were found during scanning
            var foundInPackages = output.externalCanonicalsRequired.Where(r => r.isMissing == false).ToArray();
            if (foundInPackages.Any())
            {
                Console.WriteLine("-----------------------------------");
                Console.WriteLine("Found the following canonical resources in dependant packages:");
                Console.WriteLine("\tResource Type\tCanonical Url\tVersion\tStatus\tPackage Id");
                foreach (var details in foundInPackages)
                {
                    Console.WriteLine($"\t{details.resourceType}\t{details.canonical}\t{details.version}\t{details.status}\t{details.foundInPackage}");
                }

                foreach (var item in foundInPackages)
                    output.externalCanonicalsRequired.Remove(item);
            }

            // Report out the left over resources
            Console.WriteLine("-----------------------------------");
            Console.WriteLine("Requires the following non-core canonical resources from a registry:");
            Console.WriteLine("\tResource Type\tCanonical Url\tVersion");
            foreach (var details in output.externalCanonicalsRequired)
            {
                Console.WriteLine($"\t{details.resourceType}\t{details.canonical}\t{details.version}");
            }
            Console.WriteLine("-----------------------------------");
        }

        [TestMethod]
        public void PrepareDependantBundleFromRegistry()
        {
            string json = System.IO.File.ReadAllText(@"C:\temp\uploadfig-dump.json");
            var output = System.Text.Json.JsonSerializer.Deserialize<OutputDependenciesFile>(json);
            Bundle bun = new Bundle();

            // Try to download each of the given resources
            var nctsServer = new FhirClient("https://api.healthterminologies.gov.au/integration/R4/fhir");
            foreach (var dc in output.externalCanonicalsRequired)
            {
                var r = nctsServer.Search(dc.resourceType, new[] { $"url={dc.canonical}" });
                if (r.Entry.Count > 1)
                {
                    // Check if these are just more versions of the same thing, then to the canonical versioning thingy
                    // to select the latest version.
                    var cv = Hl7.Fhir.WebApi.CurrentCanonical.Current(r.Entry.Select(e => e.Resource as IVersionableConformanceResource));

                    // remove all the others that aren't current.
                    r.Entry.RemoveAll(e => e.Resource != cv as Resource);
                }
                bun.Entry.AddRange(r.Entry);
                Assert.IsNotNull(r);
            }
            System.IO.File.WriteAllText(@"c:\temp\uploadfig-dependencies.json", new r4b.Hl7.Fhir.Serialization.FhirJsonSerializer(new SerializerSettings() { Pretty = true }).SerializeToString(bun));
        }
    }
}