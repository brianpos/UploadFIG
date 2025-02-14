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
		/// <remarks>
		/// This is really only useful in debugging to continue to use the last downloaded version of the package
		/// (particularly when using raw packages from the web and not registry - often used for testing CI builds)
		/// </remarks>
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
		/// (Is used as a filter on the root package only)
        /// </summary>
        public List<string> ResourceTypes { get; set; }

        /// <summary>
        /// Only process these selected files (Type/Id)
		/// (Root package only, dependent profiles will be loaded as required)
        /// </summary>
        public List<string> SelectFiles { get; set; }

		/// <summary>
		/// Any specific files that should be ignored/skipped when processing the package
		/// (Is used as a filter on the root package only)
		/// </summary>
		public List<string> IgnoreFiles { get; set; }

		/// <summary>
		/// Any specific Canonical URls that should be ignored/skipped when processing dependencies of resources
		/// (applies to all packages when scanning dependencies)
		/// </summary>
		/// <remarks>
		/// This will check for versioned, or un-versioned canonicals
		/// e.g. Filtering a versioned canonical will only remove explicit references to that version
		/// Filtering an un-versioned canonical will remove all references to that canonical (versioned or not)
		/// </remarks>
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
        /// The format of the content to upload to the destination FHIR server (xml/json)
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
        /// Check and clean any narratives in the package and remove suspect ones (based on the Microsoft FHIR Server's rules)
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
		/// Remove all snapshots in StructureDefinitions
		/// </summary>
		public bool RemoveSnapshots { get; set; }

		/// <summary>
		/// Patch canonical URL references to be version specific where they resolve within the package
		/// </summary>
		public bool PatchCanonicalVersions { get; set; }

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
		/// (Only applied to root package)
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
		/// The filename to write a json transaction bundle to write all of the resources to (could be used in place of directly deploying the IG)
		/// </summary>
		public string OutputTransactionBundle { get; set; }

		/// <summary>
		/// The filename to write a json collection bundle to write all of the resources to (could be used in place of directly deploying the IG)
		/// </summary>
		public string OutputCollectionBundle { get; set; }

		/// <summary>
		/// The filename of a file to write the discovered dependencies of this IG to
		/// </summary>
		public string OutputDependenciesFile { get; set; }

		/// <summary>
		/// The URL of an external FHIR registry to use for resolving dependencies
		/// </summary>
		public string ExternalRegistry { get; set; }

		/// <summary>
		/// Additional headers to supply when accessing the external FHIR registry
		/// </summary>
		public List<string> ExternalRegistryHeaders { get; set; }

		/// <summary>
		/// The filename of a file to write the json bundle of downloaded registry resources to
		/// </summary>
		public string ExternalRegistryExportFile { get; set; }

		/// <summary>
		/// The URL of an external terminology server to use for resolving terminology dependencies
		/// for expansions where a local terminology server is not available, and is too complex
		/// for the Firely SDK's built-in terminology service to handle
		/// </summary>
		public string ExternalTerminologyServer { get; set; } // = "https://r4.ontoserver.csiro.au/fhir"; // = "https://tx.dev.hl7.org.au/fhir"

		/// <summary>
		/// Additional headers to supply when accessing the external FHIR registry
		/// </summary>
		public List<string> ExternalTerminologyServerHeaders { get; set; }

		/// <summary>
		/// When leveraging an external terminology server, the maximum number of codes to expand to
		/// (where the Firely SDK can't handle the ValueSet itself)
		/// </summary>
		public long? MaxExpansionSize { get; set; } = 1000;
	}
}