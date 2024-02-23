// See https://aka.ms/new-console-template for more information
// using AngleSharp;

namespace UploadFIG
{
    public enum upload_format
    {
        xml,
        json
    }

    public class Settings
    {
        /// <summary>
        /// The explicit path of a package to process (over-rides PackageId/Version)
        /// </summary>
        /// <remarks>Optional: If not provided, will use the PackageId/Version from the HL7 FHIR Package Registry</remarks>
        public string SourcePackagePath { get; set; }

        /// <summary>
        /// Always download the file even if there is a local copy
        /// </summary>
        public bool ForceDownload { get; set; } = true;

        /// <summary>
        /// The Package ID of the package to upload (from the HL7 FHIR Package Registry)
        /// </summary>
        /// <remarks>Optional if using the PackagePath - will check that it's registered and has this package ID</remarks>
        public string PackageId { get; set; }

        /// <summary>
        /// The version of the Package to upload (from the HL7 FHIR Package Registry)
        /// </summary>
        /// <remarks>Optional if using the PackagePath, Required if using PackageID</remarks>
        public string PackageVersion { get; set; }

        /// <summary>
        /// Which resource types should be processed by the uploader
        /// </summary>
        public List<string> ResourceTypes { get; set; }

        /// <summary>
        /// Only process these selected files (Type/Id)
        /// </summary>
        public List<string> SelectFiles { get; set; }

        /// <summary>
        /// Any specific files that should be ignored/skipped when processing the package
        /// </summary>
        public List<string> IgnoreFiles { get; set; }

        /// <summary>
        /// Any specific Canonical URls that should be ignored/skipped when processing the package
        /// </summary>
        public List<string> IgnoreCanonicals { get; set; }

        /// <summary>
        /// The URL of the FHIR Server to upload the package contents to
        /// </summary>
        /// <remarks>If the TestPackageOnly is used, this is optional</remarks>
        public string DestinationServerAddress { get; set; }

        /// <summary>
        /// Headers to add to the request to the destination FHIR Server
        /// </summary>
        public List<string> DestinationServerHeaders { get; set; }

        /// <summary>
        /// The format of the content to upload to the destination FHIR server
        /// </summary>
        public upload_format? DestinationFormat { get; set; }

        /// <summary>
        /// Only perform download and static analysis checks on the Package.
        /// Does not require a DestinationServerAddress, will not try to connect to one if provided
        /// </summary>
        public bool TestPackageOnly { get; set; }

        /// <summary>
        /// Include more extensive testing on Questionnaires (experimental)
        /// </summary>
        public bool ValidateQuestionnaires { get; set; }

        /// <summary>
        /// Check and clean any narratives in the package and remove suspect ones (based on the MS FHIR Server's rules)
        /// </summary>
        public bool CheckAndCleanNarratives { get; set; }

        /// <summary>
        /// Generate the snapshots for any missing snapshots in StructureDefinitions
        /// </summary>
        public bool GenerateSnapshots { get; set; }

		/// <summary>
		/// Re-Generate all snapshots in StructureDefinitions
		/// </summary>
		public bool ReGenerateSnapshots { get; set; }

		/// <summary>
		/// Permit the tool to upload canonical resources even if they would result in the server having multiple canonical versions of the same resource after it runs
		/// </summary>
		/// <remarks>
		/// The requires the server to be able to handle resolving canonical URLs to the correct version of the resource desired by a particular call.
		/// Either via the versioned canonical reference, or using the logic defined in the $current-canonical operation
		/// </remarks>
		public bool PreventDuplicateCanonicalVersions { get; set; } = true;

        /// <summary>
        /// Download and check the package and compare with the contents of the FHIR Server,
        /// but do not update any of the contents of the FHIR Server
        /// </summary>
        public bool CheckPackageInstallationStateOnly { get; set; }

        /// <summary>
        /// Provide verbose output while processing (i.e. All filenames)
        /// </summary>
        public bool Verbose { get; set; }

        /// <summary>
        /// Specifically include processing of examples folder
        /// </summary>
        public bool IncludeExamples { get; set; }

        /// <summary>
        /// Upload resources that are referenced by our IG (directly or indirectly - excluding core/terminology/extensions packs)
        /// </summary>
        public bool IncludeReferencedDependencies { get; set; }

		/// <summary>
		/// validate resources that are referenced by our IG (directly or indirectly - excluding core/terminology/extensions packs)
		/// </summary>
		public bool ValidateReferencedDependencies { get; set; }

        /// <summary>
        /// The filename of a file to write the discovered dependencies of this IG to
        /// </summary>
        public string OutputDependenciesFile { get; set; }
    }
}