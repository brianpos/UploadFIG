# UploadFIG - Sample FHIR Implementation Guide (FIG) Uploader R4B
This is a quick c# POC to demonstrate how a FHIR Implementation Guide can be downloaded from the web
(using either a direct download, or the Firely Package Manager NuGet package)
And then upload the executable resource types contained into a FHIR Server.

During the upload step the utility will:
* GET the resource ID directly
	* compare if the resource has changed (excluding meta.versionId, meta.lastUpdated and text)
	* skip if the resource is the same
* search for the resource by canonical URL (if it is a canonical resource)
	* verify that there is not another resource on the server already using that canonical URL (hence uploading may cause issues resolving)
	* verify that the version hasn't been messed with

During the processing this utility will:
* Validate any fhirpath invariants in profiles
* Validate any search parameters included


## Running the utility
``` txt
Usage:
  UploadFIG [options]

Options:
  -s, --sourcePackagePath <sourcePackagePath>                The explicit path of a package to process (over-rides
                                                             PackageId/Version)
  -pid, --packageId <packageId>                              The Package ID of the package to upload (from the HL7 FHIR Package
                                                             Registry)
  -pv, --packageVersion <packageVersion>                     The version of the Package to upload (from the HL7 FHIR Package
                                                             Registry)
  -r, --resourceTypes <resourceTypes>                        Which resource types should be processed by the uploader 
                                                             [default: StructureDefinition|ValueSet|CodeSystem|SearchParameter
                                                             |ConceptMap|StructureMap]
  -if, --ignoreFiles <ignoreFiles>                           Any specific files that should be ignored/skipped when processing the
                                                             package
  -ic, --ignoreCanonicals <ignoreCanonicals>                 Any specific Canonical URls that should be ignored/skipped when
                                                             processing the package
  -d, --destinationServerAddress <destinationServerAddress>  The URL of the FHIR Server to upload the package contents to
  -h, --destinationServerHeaders <destinationServerHeaders>  Headers to add to the request to the destination FHIR Server
  -t, --testPackageOnly                                      Only perform download and static analysis checks on the Package.
                                                             Does not require a DestinationServerAddress, will not try to connect
                                                             to one if provided
                                                             [default: False]
  -c, --checkPackageInstallationStateOnly                    Download and check the package and compare with the contents of the
                                                             FHIR Server, but do not update any of the contents of the FHIR Server
                                                             [default: False]
  --includeExamples                                          Also include files in the examples sub-directory
                                                             (Still needs resource type specified)
  --verbose                                                  Provide verbose diagnostic output while processing
                                                             (e.g. Filenames processed)
                                                             [default: False]
  --version                                                  Show version information
  -?, -h, --help                                             Show help and usage information
```

## Examples
### Review the SDOH Clinical Care IG Package
Test the pacakge content and not try and upload any data to a server, and will grab the latest
version from the HL7 FHIR Package Registry
> UploadFIG -pid hl7.fhir.us.sdoh-clinicalcare  -t


### Verify an installation of the US Core v6.1.0
Check to see if the US Core IG Package v6.1.0 is loaded onto a local server, and if any content has changed
> UploadFIG -pid hl7.fhir.us.core -pv 6.1.0 -c -d https://localhost:44348 --verbose

### Skip processing of a specific file
> UploadFIG -d https://localhost:44348 -pid hl7.fhir.au.base -pv 4.0.0 --verbose -if package/StructureDefinition-medication-brand-name.json

### Direct download a specific package
> UploadFIG -d https://localhost:44348 -s https://example.org/demo-package.tgz -t --verbose

### Test a locally built package
> UploadFIG -s E:\git\HL7\fhir-sdoh-clinicalcare-publish\output\package.r4b.tgz -t --verbose
