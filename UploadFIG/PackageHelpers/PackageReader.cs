using Firely.Fhir.Packages;
using Hl7.Fhir.Rest;
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.Linq;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UploadFIG.PackageHelpers;

namespace UploadFIG
{
    /// <summary>
    /// Set of helper functions to read FHIR Packages
    /// </summary>
    public static class PackageReader
    {
        /// <summary>
        /// Read the FHIR Package Manifest from a TGZ in a stream
        /// </summary>
        /// <param name="sourceStream"></param>
        /// <returns></returns>
        public static PackageManifest ReadManifest(Stream sourceStream)
        {
            if (sourceStream.Position != 0)
                sourceStream.Position = 0;
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
                            var result = PackageParser.ParseManifest(content);
                            PatchManifestWithKnownIssues(result);
                            return result;
                        }
                    }
                }
            }
            return null;
        }

        private static void PatchManifestWithKnownIssues(PackageManifest result)
        {
            //if (result.Name == "hl7.terminology.r4" && result.Version == "6.1.0")
            //{
            //	result.Dependencies.Add("hl7.fhir.uv.extensions.r4", "5.1.0");
            //}
        }

        public static PackageIndex ReadPackageIndex(Stream sourceStream)
        {
            if (sourceStream.Position != 0)
                sourceStream.Position = 0;
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

        static Regex _matches = new Regex("^hl7\\.fhir\\.r\\d+[A-Za-z]?\\.(core|expansions|examples|search|elements|corexml)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static PackageDetails ReadPackageIndexDetails(Stream sourceStream, TempPackageCache cache, string logTabPrefix = "")
        {
            var manifest = ReadManifest(sourceStream);
            var index = ReadPackageIndex(sourceStream);

            Console.WriteLine($"{logTabPrefix}{manifest.Name}|{manifest.Version}");

            PackageDetails result = new PackageDetails() {
                packageId = manifest.Name,
                packageVersion = manifest.Version,
                Files = index.Files,
            };

            if (manifest.Dependencies != null)
            {
                foreach (var dependent in manifest.Dependencies)
                {
                    // Skip any dependencies where the following regex is matched
                    // "^hl7\\.fhir\\.r\\d+[A-Za-z]?\\.(core|expansions|examples|search|elements|corexml)$"
                    // Thanks Gino

                    if (_matches.IsMatch(dependent.Key))
                        continue;

                    // Grab the dependent package
                    var packageStream = cache.GetPackageStream(dependent.Key, dependent.Value, out var leaveOpen);
                    if (packageStream != null)
                    {
                        try
                        {
                            var dependentDetails = ReadPackageIndexDetails(packageStream, cache, logTabPrefix + "    ");
                            result.dependencies.Add(dependentDetails);
                        }
						finally
						{
							if (!leaveOpen)
								packageStream.Dispose();
						}
					}
                }
            }
            return result;
        }

        public static String? ReadResourceContent(Stream sourceStream, string filename)
        {
            if (sourceStream.Position != 0)
                sourceStream.Position = 0;
            try
            {
                Stream gzipStream = new System.IO.Compression.GZipStream(sourceStream, System.IO.Compression.CompressionMode.Decompress, true);
                using (gzipStream)
                {
                    var reader = new TarReader(gzipStream);
                    using (reader)
                    {
                        TarEntry? entry;
                        while ((entry = reader.GetNextEntry()) != null)
                        {
                            if (entry.EntryType == TarEntryType.Directory)
                                continue;
                            // Read the package definition file
                            if (entry.Name == "package/" + filename)
                            {
                                var stream = entry.DataStream;
                                if (stream != null)
                                {
                                    using (stream)
                                    {
                                        StreamReader sr = new StreamReader(stream);
                                        using (sr)
                                        {
                                            return sr.ReadToEnd();
                                        }
                                    }
                                }
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
    }
}
