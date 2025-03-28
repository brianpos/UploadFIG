# ![UploadFIG logo](logo_tiny.png "UploadFIG logo") UploadFIG - Command Line Parameters

See the [examples](examples.md) page for examples of the below parameters.

``` txt
Usage:
  UploadFIG [options]

Options:
  -s, --sourcePackagePath <path>                             The explicit path of a package to process (over-rides
                                                             PackageId/Version)
  -pid, --packageId <packageId>                              The Package ID of the package to upload (from the HL7 FHIR Package
                                                             Registry)
  -fd, --forceDownload                                       Force the download of the package from the source package path
                                                             (Useful if immediately running multiple times with `false` to use the last downloaded package)
                                                             [default: True]
  -fv, --fhirVersion <R4|R4B|R5>                             Force the engine to a specific FHIR Version.
                                                             If the IG itself is a different version, then the tool will abort
                                                             *Required if using a wildcard pattern to deploy a collection of raw resource files*
  -pv, --packageVersion <version>                            The version of the Package to upload (from the HL7 FHIR Package
                                                             Registry)
  -r, --resourceTypes <typename>                             Which resource types should be processed by the uploader
                                                             Note that `*` can be used to permit ALL types to be uploaded
                                                             [default: StructureDefinition|ValueSet|CodeSystem|Questionnaire
                                                             |SearchParameter|ConceptMap|StructureMap|Library]
  -sf, --selectFiles <filename>                              Only process these selected files
                                                             (e.g. package/SearchParameter-valueset-extensions-ValueSet-end.json)
  -ap, --AdditionalPackages <packageId|ver>                  Set of additional packages to include in the processing
                                                             These will be processes as though they are dependencies of the root package
  -if, --ignoreFiles <filename>                              Any specific files that should be ignored/skipped when processing the
                                                             package
  -ic, --ignoreCanonicals <URL>                              Any specific Canonical URls that should be ignored/skipped when
                                                             processing the package and resource dependencies
  -ip, --ignorePackages <packageId|ver>                      While loading in dependencies, ignore these versioned packages. 
                                                             e.g. us.nlm.vsac|0.18.0
  -d, --destinationServerAddress <URL>                       The URL of the FHIR Server to upload the package contents to
  -dh, --destinationServerHeaders <header>                   Headers to add to the request to the destination FHIR Server
                                                             e.g. `Authentication: Bearer xxxxxxxxxxx`
  -df, --destinationFormat <json|xml>                        The format to upload to the destination server
                                                             [default: xml]
  -t, --testPackageOnly                                      Only perform download and static analysis checks on the Package.
                                                             Does not require a DestinationServerAddress, will not try to connect
                                                             to one if provided
                                                             [default: False]
  -vq, --validateQuestionnaires                              Include more extensive testing on Questionnaires (experimental)
                                                             [default: False]
  -vrd, --validateReferencedDependencies                     Validate any referenced resources from dependencies being installed 
                                                             [default: False]
  -pdv, --preventDuplicateCanonicalVersions                  Permit the tool to upload canonical resources even if
                                                             they would result in the server having multiple canonical
                                                             versions of the same resource after it runs
                                                             The requires the server to be able to handle resolving
                                                             canonical URLs to the correct version of the resource
                                                             desired by a particular call. Either via the versioned
                                                             canonical reference, or using the logic defined in the
                                                             $current-canonical operation
                                                             [default: True]
  -cn, --checkAndCleanNarratives                             Check and clean any narratives in the package and remove suspect ones
                                                             (based on the MS FHIR Server's rules)
                                                             [default: False]
  -sn, --stripNarratives                                     Strip all narratives from the resources in the package
                                                             [default: False]
  -c, --checkPackageInstallationStateOnly                    Download and check the package and compare with the contents of the FHIR Server,
                                                             but do not update any of the contents of the FHIR Server
                                                             [default: False]
  -gs, --generateSnapshots                                   Generate the snapshots for any missing snapshots in StructureDefinitions
                                                             [default: False]
  -rs, --regenerateSnapshots                                 Re-Generate all snapshots in StructureDefinitions
                                                             [default: False]
  -rms, --removeSnapshots                                    Remove all snapshots in StructureDefinitions
                                                             [default: False]
  -pcv, --patchCanonicalVersions                             Patch canonical URL references to be version specific if they resolve within the package [default: False]
  --includeReferencedDependencies                            Upload any referenced resources from resource dependencies being included
                                                             [default: False]
  --includeExamples                                          Also include files in the examples sub-directory
                                                             (Still needs resource type specified)
                                                             [default: False]
  --verbose                                                  Provide verbose diagnostic output while processing
                                                             (e.g. Filenames processed)
                                                             [default: False]
  -of, --outputBundle <filename>                             The filename to write a json batch bundle containing all of the processed resources into (could be used in place of directly deploying the IG)
  -odf, --outputDependenciesFile <filename>                  Write the list of dependencies discovered in the IG into a json file for post-processing
  -reg, --externalRegistry <URL>                             The URL of an external FHIR server to use for resolving resources not already on the destination server
  -regh, --externalRegistryHeaders <header>                  Additional headers to supply when connecting to the external FHIR server
  -rego, --externalRegistryExportFile <filename>             The filename of a file to write the json bundle of downloaded registry resources
  -ets, --externalTerminologyServer <URL>                    The URL of an external FHIR terminology server to use for creating expansions (where not on an external registry)
  -etsh, --externalTerminologyServerHeaders <header>         Additional headers to supply when connecting to the external FHIR terminology server
  -mes, --maxExpansionSize <number>                          The maximum number of codes to include in a ValueSet expansion
                                                             [default: 1000]
  --version                                                  Show version information
  -?, -h, --help                                             Show help and usage information
```

> **Note:** The parameters `-r`, `-sf`, `-ap`, `-if`, `-ic`, `-ip`, `-dh`, `-regh` and `-etsh` can be repeated to provide multiple values.<br/>
> For example, `-dh "header1:value1" -dh "header2:value2"`<br/>
> *Different command line processors may require quotes if the parameter values have spaces*

> **Note:** The `-of` flag has some limitations, and is not a full replacement for the direct deployment of the IG to the server.
> The conditional updates used with canonical resources always uses server assigned IDs, thus will with use the pre-defined resource ID if it is not found.
> The direct update process compares the content with what is already deployed, and only updates the content if it has changed.
> Along with safely managing the resources IDs and canonical Versions. Without knowing what is already on the server, this is not possible to completely manage this.
