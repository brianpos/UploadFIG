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

                if (dp.Value.StartsWith("current") || dp.Value == "dev")
                {
                    // Bail for non registry CI content
                    continue;
                }

                // download if the package is not already in the cache
                string packageFile = System.IO.Path.Combine(cacheFolder, dp.Key + "_" + dp.Value.Replace(".", "_") + ".tgz");
                PackageManifest manifest;
                PackageIndex index;
                Stream packageStream;
                if (!System.IO.File.Exists(packageFile))
                {
                    // Now download the package
                    PackageClient pc = PackageClient.Create();
                    var rawPackage = pc.GetPackage(new PackageReference(dp.Key, dp.Value)).Result;
                    System.IO.File.WriteAllBytes(packageFile, rawPackage);
                    packageStream = new MemoryStream(rawPackage);
                    manifest = ReadManifest(packageStream);
                    packageStream.Position = 0;
                    index = ReadPackageIndex(packageStream);
                }
                else
                {
                    packageStream = File.OpenRead(packageFile);
                    manifest = ReadManifest(packageStream);
                    packageStream.Position = 0;
                    index = ReadPackageIndex(packageStream);
                }
                packageStream.Position = 0;

                using (packageStream)
                {
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
                                var content = ReadResource(packageStream, files.First().filename);
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
                                    }
                                    bun.AddResourceEntry(resource, $"{resource.TypeName}/{resource.Id}");
                                }
                            }
                        }
                        // output.externalCanonicalsRequired[0].isMissing = false;
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

        private static String ReadResource(Stream sourceStream, string filename)
        {
            sourceStream.Position = 0;
            try
            {
                Stream gzipStream = new System.IO.Compression.GZipStream(sourceStream, System.IO.Compression.CompressionMode.Decompress, true);
                using (gzipStream)
                {
                    var reader = new TarReader(gzipStream);
                    TarEntry entry;
                    while ((entry = reader.GetNextEntry()) != null)
                    {
                        if (entry.EntryType == TarEntryType.Directory)
                            continue;
                        // Read the package definition file
                        if (entry.Name == "package/" + filename)
                        {
                            var stream = entry.DataStream;
                            using (stream)
                            {
                                StreamReader sr = new StreamReader(stream);
                                return sr.ReadToEnd();
                            }
                        }
                    }
                }
            }
            catch (System.IO.InvalidDataException ex)
            {
                Console.Write($"Error trying to read {filename} from package");
            }
            return null;
        }

        public static PackageManifest? ReadManifest(Stream sourceStream)
        {
            Stream gzipStream = new System.IO.Compression.GZipStream(sourceStream, System.IO.Compression.CompressionMode.Decompress, true);
            using (gzipStream)
            {
                var reader = new TarReader(gzipStream);
                TarEntry entry;
                while ((entry = reader.GetNextEntry()) != null)
                {
                    if (entry.EntryType == TarEntryType.Directory)
                        continue;
                    // Read the package definition file
                    if (entry.Name == "package/package.json")
                    {
                        var stream = entry.DataStream;
                        using (stream)
                        {
                            StreamReader sr = new StreamReader(stream);
                            var content = sr.ReadToEnd();
                            return PackageParser.ParseManifest(content);
                        }
                    }
                }
            }
            return null;
        }

        public static PackageIndex? ReadPackageIndex(Stream sourceStream)
        {
            Stream gzipStream = new System.IO.Compression.GZipStream(sourceStream, System.IO.Compression.CompressionMode.Decompress, true);
            using (gzipStream)
            {
                var reader = new TarReader(gzipStream);
                TarEntry entry;
                while ((entry = reader.GetNextEntry()) != null)
                {
                    if (entry.EntryType == TarEntryType.Directory)
                        continue;
                    // Read the package definition file
                    if (entry.Name == "package/.index.json")
                    {
                        var stream = entry.DataStream;
                        using (stream)
                        {
                            StreamReader sr = new StreamReader(stream);
                            var content = sr.ReadToEnd();
                            return System.Text.Json.JsonSerializer.Deserialize<PackageIndex>(content);
                        }
                    }
                }
            }
            return null;
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