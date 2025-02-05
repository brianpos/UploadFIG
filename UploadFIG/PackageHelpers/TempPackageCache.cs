using Firely.Fhir.Packages;
using Hl7.Fhir.Rest;
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.Linq;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Threading.Tasks;

namespace UploadFIG
{
    public class TempPackageCache
    {
        public TempPackageCache()
        {
            // get the name of a new folder in the User's local temp directory
            _cacheFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UploadFIG", "PackageCache");
            if (!Directory.Exists(_cacheFolder))
                Directory.CreateDirectory(_cacheFolder);
            else
            {
                // TODO: Check if there is any "old" content in here and delete that
            }
        }

        string _cacheFolder;

        /// <summary>
        /// Return an opened stream for this specific version of the package (downloading if not present)
        /// </summary>
        /// <param name="packageId"></param>
        /// <param name="version"></param>
        /// <returns>A stream or null if the package did not exist</returns>
        public Stream GetPackageStream(string packageId, string version)
        {
            if (version.StartsWith("current") || version == "dev")
            {
                // Bail for non registry CI content
                return null;
            }

            try
            {
                // download if the package is not already in the cache
                string packageFile = System.IO.Path.Combine(_cacheFolder, packageId + "_" + version.Replace(".", "_") + ".tgz");
                if (!System.IO.File.Exists(packageFile))
                {
                    // Now download the package from the registry into the cache
                    PackageClient pc = PackageClient.Create();
                    var rawPackage = pc.GetPackage(new PackageReference(packageId, version)).WaitResult();
                    System.IO.File.WriteAllBytes(packageFile, rawPackage);
                    return new MemoryStream(rawPackage);
                }

                // Package is already here, just return a stream on it
                return File.OpenRead(packageFile);
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Failed to retrieve the package {packageId}|{version}: {ex.Message}");
                return null;
            }
        }
    }
}
