using Firely.Fhir.Packages;
using Hl7.Fhir.Rest;
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.Linq;
using System.Runtime.Intrinsics.Arm;
using System.Text;
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
                            return PackageParser.ParseManifest(content);
                        }
                    }
                }
            }
            return null;
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
					if (dependent.Key == "hl7.fhir.r4.core")
						continue;
					if (dependent.Key == "hl7.fhir.r5.core")
						continue;
					if (dependent.Key == "hl7.fhir.r4.examples")
						continue;
					if (dependent.Key == "hl7.fhir.r5.examples")
						continue;

					// Grab the dependent package
					var packageStream = cache.GetPackageStream(dependent.Key, dependent.Value);
					if (packageStream != null)
					{
						using (packageStream)
						{
							var dependentDetails = ReadPackageIndexDetails(packageStream, cache, logTabPrefix + "    ");
							result.dependencies.Add(dependentDetails);
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
