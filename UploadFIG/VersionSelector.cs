using Firely.Fhir.Packages;
using Hl7.Fhir.Model;

namespace UploadFIG
{
    internal class VersionSelector
    {
        public static FHIRVersion? SelectVersion(Firely.Fhir.Packages.PackageManifest manifest)
        {
            var version = manifest.GetFhirVersion();
            switch (version)
            {
                case "4.0.1":
                    return FHIRVersion.N4_0;
                case "4.3.0":
                    return FHIRVersion.N4_3;
                case "5.0.0":
                    return FHIRVersion.N5_0;
            }

            return null;
        }
    }
}
