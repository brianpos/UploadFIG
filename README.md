# UploadFIG - FHIR Implementation Guide (FIG) Uploader R4/R4B/R5
![UploadFIG logo](logo_small.png "UploadFIG logo")

This tool provides a way to deploy a FHIR Implementation Guide to a FHIR Server.
The content can be loaded from:
* (-pid) the fhir registry via packageID
* (-s) an explicit web location (complete source URL including filename where applicable)
* (-s) a file on the local filesystem

During the upload step the utility will:
* GET the resource ID directly
    * compare if the resource has changed (excluding meta.versionId, meta.lastUpdated and text)
    * skip if the resource is the same
* search for the resource by canonical URL (if it is a canonical resource)
    * verify that there is not another resource on the server already using that canonical URL (hence uploading may cause issues resolving)<br/>
    *(can be disabled via -pdv false)*
    * verify that the version hasn't been messed with

During the processing this utility will:
* Validate any fhirpath invariants in profiles
* Validate any search parameters included
(Note: These validation results should be verified as correct and investigate if they would impact the operation of the guide in your environment/toolchain)

## Resource IDs
While uploading the package content the utility will attempt to find the resource on the server using the following methods:
* Example resources: simple read by Resource ID <br/>*(e.g. GET [base]/[ResourceType]/[ResourceID])*
    - always uses PUT to update the resource
    - This will overwrite any existing resource with the same ID
* Canonical resources: search via canonical URL and canonical version <br/>*(e.g. GET [base]/[ResourceType]?url=[CanonicalUrl]&version=[CanonicalVersion])*
    - PUT if the canonical resource matches a record by canonical URL/Version
    - "refreshes" or brings the resource back to a known good state
    - POST for any new resources
    - Multiple resources with different canonical version numbers found with the same canonical are reported in the output
    - Multiple resources with the same canonical version number are rejected and must be resolved manually before the resource can be processed


## Running the utility
``` txt
Usage:
  UploadFIG [options]

Options:
  -s, --sourcePackagePath <sourcePackagePath>                The explicit path of a package to process (over-rides
                                                             PackageId/Version)
  -fd, --forceDownload                                       Force the download of the package from the source package path
                                                             (If not specified, will use the last downloaded package)
                                                             [default: True]
  -pid, --packageId <packageId>                              The Package ID of the package to upload (from the HL7 FHIR Package
                                                             Registry)
  -pv, --packageVersion <packageVersion>                     The version of the Package to upload (from the HL7 FHIR Package
                                                             Registry)
  -r, --resourceTypes <resourceTypes>                        Which resource types should be processed by the uploader 
                                                             [default: StructureDefinition|ValueSet|CodeSystem|Questionnaire
                                                             |SearchParameter|Library|ConceptMap|StructureMap]
  -sf, --selectFiles <selectFiles>                           Only process these selected files
                                                             e.g. package/SearchParameter-valueset-extensions-ValueSet-end.json
  -if, --ignoreFiles <ignoreFiles>                           Any specific files that should be ignored/skipped when processing the
                                                             package
  -ic, --ignoreCanonicals <ignoreCanonicals>                 Any specific Canonical URls that should be ignored/skipped when
                                                             processing the package
  -d, --destinationServerAddress <destinationServerAddress>  The URL of the FHIR Server to upload the package contents to
  -dh, --destinationServerHeaders <destinationServerHeaders> Headers to add to the request to the destination FHIR Server
                                                             e.g. `Authentication: Bearer xxxxxxxxxxx`
  -df, --destinationFormat                                   The format to upload to the destination server
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
  -c, --checkPackageInstallationStateOnly                    Download and check the package and compare with the contents of the
                                                             FHIR Server, but do not update any of the contents of the FHIR Server
                                                             [default: False]
  -gs, --generateSnapshots                                   Generate the snapshots for any missing snapshots in StructureDefinitions
                                                             [default: False]
  -rs, --regenerateSnapshots                                 Re-Generate all snapshots in StructureDefinitions
                                                             [default: False]
  --includeReferencedDependencies                            Upload any referenced resources from resource dependencies being included
                                                             [default: False]
  --includeExamples                                          Also include files in the examples sub-directory
                                                             (Still needs resource type specified)
  --verbose                                                  Provide verbose diagnostic output while processing
                                                             (e.g. Filenames processed)
                                                             [default: False]
  -otb, --outputTransactionBundle <filename>                 The filename to write a json transaction bundle to write all of the resources to (could be used in place of directly deploying the IG)
  -ocb, --outputCollectionBundle <filename>                  The filename to write a json collection bundle to write all of the resources to (could be used in place of directly deploying the IG)
                                                             (could be used in place of directly deploying the IG - has limitations noted below)
  -odf, --outputDependenciesFule <filename>                  Write the list of dependencies discovered in the IG into a json file for post-processing
  -reg, --externalRegistry <externalRegistry>                The URL of an external FHIR server to use for resolving resources not already on the destination server []
  -regh, --externalRegistryHeaders <headers>                 Additional headers to supply when connecting to the external FHIR server
  -rego, --externalRegistryExportFile <filename>             The filename of a file to write the json bundle of downloaded registry resources
  -ets, --externalTerminologyServer <URL>                    The URL of an external FHIR terminology server to use for creating expansions (where not on an external registry)
  -etsh, --externalTerminologyServerHeaders <headers>        Additional headers to supply when connecting to the external FHIR terminology server
  -mes, --maxExpansionSize <number>                          The maximum number of codes to include in a ValueSet expansion
                                                             [default: 1000]
  --version                                                  Show version information
  -?, -h, --help                                             Show help and usage information
```

> **Note:** The `-otb` and `-ocb` flag has some limitations, and is not a full replacement for the direct deployment of the IG to the server.
> It is not able to cleanly handle the conditional updates that are required for canonical resources, and will not be able to update the server with the correct resource ID.
> The actual update process to a server compares the content with what is already deployed, and only updates the content if it has changed.
> Along with safely managing the resources IDs and canonical Versions. Without knowing what is already on the server, this is not possible to correctly manage.

## Installation
As a dotnet tool installation is done through the commandline which will download the latest version from nuget.org
(I've included the command to install it into the global dotnet application folder, but you can install it into a local folder if you prefer)
``` ps
PS C:\Users\brian> dotnet tool install uploadfig --global
You can invoke the tool using the following command: UploadFIG
Tool 'uploadfig' (version '2023.8.2.2') was successfully installed.
PS C:\Users\brian> 
```

### Updating
``` ps
PS C:\Users\brian> dotnet tool update uploadfig --global
Tool 'uploadfig' was successfully updated from version '2023.8.2.2' to version '2023.8.3.15'.
PS C:\Users\brian> 
```

### Uninstalling
``` ps
PS C:\Users\brian> dotnet tool uninstall uploadfig --global
Tool 'uploadfig' (version '2023.8.3.15') was successfully uninstalled.
```

## Handling Package Dependencies
FHIR Packages can have dependencies on other FHIR Packages. These dependencies can be direct or indirect.
The utility uses the FHIR package registry to locate dependent packages, and will download them automatically.
It currently only supports explicitly versioned package references, and not indeterminate ones like `current`.

These will always be downloaded from the FHIR Package Registry, and not from the local file system, and 
will be used to detect if there are any issues with the packages, and what actual dependent canonical resources 
are actually required by the package being loaded - which helps to identify if there will be missing content
that needs to come from some other source, such as a live registry for some terminologies that aren't
available as FHIR packages, and thus not included.

### Uploading Dependencies
In general there are 3 options that can be considered with deploying the dependencies of a package
1. Do not load any dependencies (just list them out - which is what the previous version of the utility did)
2. Load in the resources in dependencies that are required (directly or indirectly) from resources in the package we are uploading
3. Load in all the resources in all the dependencies

This utility will still perform option 1 by default, and can now perform option 2 if the `--includeReferencedDependencies` option is specified.
If your environment requires ALL resources from the IGs listed in the dependencies to be loaded, 
then you will need to run the utility multiple times.

### Caching
The dependency packages are downloaded into the users temp folder and cached there for future use.
The utility will check the cache first before downloading the package again, based on the packageID and package version.

The packages are not unpacked and processed as raw files on disk, but are processed directly from the tgz file in memory.
This can save space, and remove the likelihood that the files will be tampered with, or package extraction not be complete for various reasons.

On Windows this is a temporary folder under the users profile folder, e.g.
`C:\Users\brian\AppData\Local\Temp\UploadFIG\PackageCache`


## Understanding the output
### Package Metadata
The first section in the output is the metadata about the package that was downloaded and is being processed.
It finishes with the list of package dependencies in the project.


### Scanning package content
This is a sample showing several examples of the kind of output from the utility when it is processing the package contents.
The section will usually be empty unless there are parsing errors while reading the package contents.

### Scanning dependencies / indirect dependencies
During this stage the utility will recursively iterate through all the dependencies of the package and build a list of all 
the resources that are referenced by the package.
Then report out if there are some resources that are not found in any of the IGs package dependencies.
For any canonical resources that cannot be resolved the resource that references it will be also reported in a line underneath.

``` txt
Scanning dependencies:

Scanning indirect dependencies:

Unable to resolve these canonical resources: 2
	Resource Type	Canonical Url	Version	Package Source
	CodeSystem	http://hl7.org/fhir/fhir-types	
					^- http://hl7.org/fhir/us/davinci-crd/ValueSet/configTypes|2.0.1	package/ValueSet-configTypes.json
	CodeSystem	urn:oid:2.16.840.1.113883.6.285	
					^- http://hl7.org/fhir/us/davinci-crd/ValueSet/serviceRequestCodes|2.0.1	package/ValueSet-serviceRequestCodes.json
```
In verbose mode the utility will also report out the list of required resources in the package dependencies, along with the resource
that required them to be included - very useful for tracing out why resources were included.

### Validate/upload dependencies:
This section is only shown if the `--includeReferencedDependencies` or `-vrd` option is used.
It shows the results of validating/loading the detected dependent resources to the fhir server.

### Validate/upload package content:
This is a sample showing several examples of the kind of output from the utility when it is processing the package contents.

Here we can see that several resources have been created on the server, and some have been updated.
Some errors were also reported while processing invariants in a StructureDefinition.
``` txt
Scanning package content:
    created     StructureDefinition     http://hl7.org.au/fhir/core/StructureDefinition/au-core-alcoholstatus|0.1.0-draft
    created     StructureDefinition     http://hl7.org.au/fhir/core/StructureDefinition/au-core-allergyintolerance|0.1.0-draft
    created     StructureDefinition     http://hl7.org.au/fhir/core/StructureDefinition/au-core-bloodpressure|0.1.0-draft
    created     StructureDefinition     http://hl7.org.au/fhir/core/StructureDefinition/au-core-bmi|0.1.0-draft
    unchanged   StructureDefinition     http://hl7.org.au/fhir/core/StructureDefinition/au-core-immunization|0.1.0-draft
    unchanged   StructureDefinition     http://hl7.org.au/fhir/core/StructureDefinition/au-core-lastmenstrualperiod|0.1.0-draft
    #---> Error validating invariant http://hl7.org.au/fhir/core/StructureDefinition/au-core-lipid-result: au-core-lipid-01
            Context: Observation
            Expression: (code.coding.code!='32309-7' and valueQuantity.value.exists()) implies (valueQuantity.unit.exists() and valueQuantity.code.exists())
            Return type: boolean
    *---> error: Operator '!=' can experience unexpected behaviours when used with a collection
            code[] != string
    *---> error: prop 'valueQuantity' is the choice type, remove the type from the end - value
    *---> error: prop 'valueQuantity' not found on Observation

    unchanged   StructureDefinition     http://hl7.org.au/fhir/core/StructureDefinition/au-core-lipid-result|0.1.0-draft
```

### Package Content Summary (Test mode only)
When run in TestMode the output will also include a table of all the canonical resources that it processed for reference.

``` txt
Package content summary: (40)
        Canonical Url  Canonical Version       Status  Name
        http://hl7.org/fhir/us/davinci-ra/CodeSystem/coding-gap-annotation      2.0.0-ballot    Active  CodingGapAnnotation
        http://hl7.org/fhir/us/davinci-ra/CodeSystem/coding-gap-task-reason     2.0.0-ballot    Draft   CodingGapTaskReason
        http://hl7.org/fhir/us/davinci-ra/CodeSystem/evidence-status    2.0.0-ballot    Active  RiskAdjustmentEvidenceStatus
        http://hl7.org/fhir/us/davinci-ra/CodeSystem/hierarchical-status        2.0.0-ballot    Active  RiskAdjustmenthierarchicalStatus
        http://hl7.org/fhir/us/davinci-ra/CodeSystem/suspect-type       2.0.0-ballot    Active  RiskAdjustmentSuspectType
```

This section will also contain lists of all dependent resources directly referenced (via canonicals) in the dependency packages,
and another section for indirectly required canonical resources.
``` txt
--------------------------------------
Requires the following non-core canonical resources: 20
	Resource Type	Canonical Url	Version	Package Source
	ValueSet	http://cts.nlm.nih.gov/fhir/ValueSet/2.16.840.1.114222.4.11.3591		(us.nlm.vsac|0.11.0)
	StructureDefinition	http://hl7.org/fhir/5.0/StructureDefinition/extension-CommunicationRequest.payload.content[x]	
	CodeSystem	http://hl7.org/fhir/fhir-types	
	StructureDefinition	http://hl7.org/fhir/tools/StructureDefinition/elementdefinition-json-name	
	StructureDefinition	http://hl7.org/fhir/tools/StructureDefinition/json-primitive-choice	
	StructureDefinition	http://hl7.org/fhir/us/core/StructureDefinition/us-core-condition		(hl7.fhir.us.core|3.1.1)
	StructureDefinition	http://hl7.org/fhir/us/core/StructureDefinition/us-core-encounter		(hl7.fhir.us.core|3.1.1)
	StructureDefinition	http://hl7.org/fhir/us/core/StructureDefinition/us-core-location		(hl7.fhir.us.core|3.1.1)
	StructureDefinition	http://hl7.org/fhir/us/core/StructureDefinition/us-core-medication		(hl7.fhir.us.core|3.1.1)
	StructureDefinition	http://hl7.org/fhir/us/core/StructureDefinition/us-core-observation-lab		(hl7.fhir.us.core|3.1.1)
	StructureDefinition	http://hl7.org/fhir/us/core/StructureDefinition/us-core-organization		(hl7.fhir.us.core|3.1.1)
	StructureDefinition	http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient		(hl7.fhir.us.core|3.1.1)
	StructureDefinition	http://hl7.org/fhir/us/core/StructureDefinition/us-core-practitioner		(hl7.fhir.us.core|3.1.1)
	StructureDefinition	http://hl7.org/fhir/us/core/StructureDefinition/us-core-practitionerrole		(hl7.fhir.us.core|3.1.1)
	ValueSet	http://hl7.org/fhir/us/core/ValueSet/us-core-medication-codes		(hl7.fhir.us.core|3.1.1)
	StructureDefinition	http://hl7.org/fhir/uv/sdc/StructureDefinition/sdc-task		(hl7.fhir.uv.sdc|3.0.0)
	CodeSystem	http://loinc.org		(hl7.terminology.r4|5.3.0)
	CodeSystem	http://www.ama-assn.org/go/cpt		(hl7.terminology.r4|5.3.0)
	CodeSystem	https://www.cms.gov/Medicare/Coding/HCPCSReleaseCodeSets		(hl7.terminology.r4|5.3.0)
	CodeSystem	urn:oid:2.16.840.1.113883.6.285	

--------------------------------------
Indirectly requires the following non-core canonical resources: 31
	Resource Type	Canonical Url	Version	Package Source
	CodeSystem	http://hl7.org/fhir/codesystem-properties-mode		(hl7.fhir.uv.extensions.r4|1.0.0)
					^- http://hl7.org/fhir/ValueSet/codesystem-properties-mode|1.0.0	(hl7.fhir.uv.extensions.r4|1.0.0)
	CodeSystem	http://hl7.org/fhir/sid/icd-10-cm		(hl7.terminology.r4|5.3.0)
					^- http://hl7.org/fhir/us/core/ValueSet/us-core-condition-code|3.1.1	(hl7.fhir.us.core|3.1.1)
	CodeSystem	http://hl7.org/fhir/sid/icd-9-cm	
					^- http://hl7.org/fhir/us/core/ValueSet/us-core-condition-code|3.1.1	(hl7.fhir.us.core|3.1.1)
	StructureDefinition	http://hl7.org/fhir/StructureDefinition/codesystem-properties-mode		(hl7.fhir.uv.extensions.r4|1.0.0)
					^- http://loinc.org|3.1.0	(hl7.terminology.r4|5.3.0)
					^- http://www.nlm.nih.gov/research/umls/rxnorm|3.0.1	(hl7.terminology.r4|5.3.0)
```
The verbose mode will also include tracing information is also shown in the directly required resources as is found in the indirect section.


### Dependency Verification (Upload mode only)
This section displays a summary of all the resource dependencies that were detected as required
by the implementation guide (e.g. extensions, profiles and terminologies referenced by a profile)
and their current state on the destination server.

This is useful to know if there is missing content on the server that may be required for validation,
or if there are some canonical resources that have multiple versions existing.
``` txt
Destination server canonical resource dependency verification:
        http://cts.nlm.nih.gov/fhir/ValueSet/2.16.840.1.113762.1.4.1    (current)       (missing)
        http://cts.nlm.nih.gov/fhir/ValueSet/2.16.840.1.113762.1.4.1010.4       (current)       (missing)
        http://cts.nlm.nih.gov/fhir/ValueSet/2.16.840.1.113762.1.4.1021.103     (current)       (missing)
        http://hl7.org/fhir/us/core/StructureDefinition/us-core-organization    (current)       6.1.0, 3.1.1
        http://hl7.org/fhir/us/core/StructureDefinition/us-core-practitioner    (current)       6.1.0, 3.1.1
        http://terminology.hl7.org/CodeSystem/cmshcc    (current)       (missing)
Done!
```
The first column here is the canonical URL, the second column is the specific version the reference is requesting, 
or the word 'current' if the reference is requesting the latest version of the resource.
The final column indicates the canonical version numbers that are currently on the destination server.


## Examples
### Review the SDOH Clinical Care IG Package
Test the package content and not try and upload any data to a server, and will grab the latest
version from the HL7 FHIR Package Registry
(and also validate any resource dependencies from package dependencies - US-Core/SDC...)
``` ps
> UploadFIG -pid hl7.fhir.us.sdoh-clinicalcare -t -vrd
```

### Verify an installation of the US Core v6.1.0
Check to see if the US Core IG Package v6.1.0 is loaded onto a local server, and if any content has changed
``` ps
> UploadFIG -pid hl7.fhir.us.core -pv 6.1.0 -c -d https://localhost:44348 --verbose
```

### Skip processing of a specific file
``` ps
> UploadFIG -d https://localhost:44348 -pid hl7.fhir.au.base -pv 4.0.0 --verbose -if package/StructureDefinition-medication-brand-name.json
```

### Direct download a specific package
(Note that you should include the forceDownload flag here to ensure that it doesn't use a locally saved file)
``` ps
> UploadFIG -d https://localhost:44348 -s https://example.org/demo-package.tgz --verbose --forceDownload
```

### Test a locally built package
``` ps
> UploadFIG -s "E:\git\HL7\fhir-sdoh-clinicalcare-publish\output\package.r4b.tgz" -t --verbose
```

### Upload AU Base to a Microsoft FHIR Server
(Note the inclusion of the -cn flag to cleanse any narratives that would be otherwise rejected by the Microsoft FHIR Server)
``` ps
> UploadFIG -d https://localhost:44348 -pid hl7.fhir.au.base -pv 4.0.0 -cn -df json -dh "Authorization:Bearer ******"
      --includeReferencedDependencies 
      -reg https://api.healthterminologies.gov.au/integration/R4/fhir -rego au-registry-content.json
      -ets https://tx.dev.hl7.org.au/fhir
```
And also the inclusion of the `-df json` to select the json format as the hosted Microsoft FHIR Server doesn't support XML
and the `--includeReferencedDependencies` flag to indicate that dependencies should be scanned (including registry if provided)
and the `-reg` flag to specify the NCTS as the external registry to use for resolving the other resources that are not in the package.
and the `-rego` flag to write a local copy of the resources downloaded from the NCTS registry.
and the `-ets` flag to request the external terminology server to create expansions for ValueSets that are too complex for the Firely SDK terminology service.

The hosted Microsoft server may require an Authorization bearer to connect too, note that you will likely need 
to quote the content if it has spaces - which is normally there (and may be different on a different OS)
*(also note that this is not the Authentication header which is a common mistake)*

### Upload the latest version of the SDC IG to a FHIR Server in JSON format
Some fhir servers may only be able to support a single format, so you can specify xml or json explicitly to use while uploading.
This is independent of the format of the content that is native inside the IG package.
``` ps
> UploadFIG -pid hl7.fhir.au.base -d https://localhost:44348 -df json
```

### Deploy the latest version of the Davinci CRD IG and dependent resource to a FHIR Server
Many IGs have other packages that they depend on, and using `includeReferencedDependencies` downloads those packages
and then uploads resources used by the primary IG from those dependencies
``` ps
> UploadFIG -pid hl7.fhir.us.davinci-crd -d https://localhost:44348 --includeReferencedDependencies
```
Extract of output from deployment:
``` txt
    Package dependencies:
    hl7.fhir.r4.core|4.0.1
    hl7.terminology.r4|5.3.0
    hl7.fhir.uv.extensions.r4|1.0.0
    hl7.fhir.us.core|3.1.1
    hl7.fhir.uv.sdc|3.0.0
    hl7.fhir.us.davinci-hrex|1.0.0
    us.nlm.vsac|0.11.0

--------------------------------------
Scanning package content:

--------------------------------------
Scanning dependencies:

Scanning indirect dependencies:

Unable to resolve these canonical resources: 2
	Resource Type	Canonical Url	Version	Package Source
	CodeSystem	http://hl7.org/fhir/fhir-types	
					^- http://hl7.org/fhir/us/davinci-crd/ValueSet/configTypes|2.0.1	package/ValueSet-configTypes.json
	CodeSystem	urn:oid:2.16.840.1.113883.6.285	
					^- http://hl7.org/fhir/us/davinci-crd/ValueSet/serviceRequestCodes|2.0.1	package/ValueSet-serviceRequestCodes.json

--------------------------------------
Validate/upload dependencies:
    created	CodeSystem	https://www.cms.gov/Medicare/Coding/HCPCSReleaseCodeSets|1.0.1	(hl7.terminology.r4|5.3.0)
    created	ValueSet	http://hl7.org/fhir/us/core/ValueSet/us-core-medication-codes|3.1.1	(hl7.fhir.us.core|3.1.1)
    created	CodeSystem	http://loinc.org|3.1.0	(hl7.terminology.r4|5.3.0)
    created	CodeSystem	http://hl7.org/fhir/codesystem-properties-mode|1.0.0	(hl7.fhir.uv.extensions.r4|1.0.0)
    created	ValueSet	http://hl7.org/fhir/ValueSet/codesystem-properties-mode|1.0.0	(hl7.fhir.uv.extensions.r4|1.0.0)
    created	CodeSystem	http://www.nlm.nih.gov/research/umls/rxnorm|3.0.1	(hl7.terminology.r4|5.3.0)

--------------------------------------
Validate/upload package content:
    created	CodeSystem	http://hl7.org/fhir/us/davinci-crd/CodeSystem/temp|2.0.1
    created	ValueSet	http://hl7.org/fhir/us/davinci-crd/ValueSet/AdditionalDocumentation|2.0.1
    created	ValueSet	http://hl7.org/fhir/us/davinci-crd/ValueSet/CMSMappableLocationCodes|2.0.1
...
    created	ValueSet	http://hl7.org/fhir/us/davinci-crd/ValueSet/taskReason|2.0.1

Destination server canonical resource dependency verification:
	http://hl7.org/fhir/fhir-types	(current)	(missing)
	http://hl7.org/fhir/us/core/ValueSet/us-core-medication-codes	(current)	3.1.1
	http://loinc.org	(current)	3.1.0
	http://www.ama-assn.org/go/cpt	(current)	(missing)
	https://www.cms.gov/Medicare/Coding/HCPCSReleaseCodeSets	(current)	1.0.1
	urn:oid:2.16.840.1.113883.6.285	(current)	(missing)
Done!
```

---

## Change history

### 21 January 2025
* Add support to reference an external terminology server to create expansions for ValueSets that are not in the external registry `-ets`, `-etsh`
* New option to output content to a file instead of uploading to a server `-of`

### 18 December 2024
* Add support to reference an external registry to resolve dependencies not in packages `-reg`, `-regh`, `-rego`
* Update Fhirpath static validation engine to resolve issue with complex extensions passing context through functions
  (found in AU base IG)

### 6 December 2024
* Update SDC questionnaire validations to handle the new `weight` function and resolve a few minor bugs in Questionnaire validation
* Update to the FirelySDK v5.11.1

### 4 April 2024
* Minor bug fix for null reference exception when scanning canonicals in some ValueSets

### 23 February 2024
* Add support for generating snapshots in StructureDefinitions
    * `-gs` to generate any missing snapshots
    * `-rs` to RE-generate ALL snapshots
* Improved validation of commandline parameters to check that either -t or -ds is provided

### 11 January 2024
* Include package/resource dependency processing and optional upload to destination FHIR server
* Include the package title in the output

### 3 January 2024
* Fix null reference error that occurs when a package contains no dependencies in the manifest
* Report an information message when detecting a search parameter with type = 'special' indicating that server requires 
  custom implementation to support
* Warnings/Information messages now displayed for search parameter checks (were suppressed if there were no errors)

### 14 December 2023
* Update fhirpath engine checks
    - Correct return type of `as()` to boolean
    - Add validation check to the `as()` function to check that the type provided could potentially be valid
    - Include `string` as one of the valid datatypes for the Search Type `Uri`
* Fhirpath validation checks now resolve `extension('http://...')` in expressions to locate the extension definition
  and validate that the extension is available in the project (or FHIR core) and then accurately constrain
  the datatype to those specified in the extension, and also the extensions defined cardinality.
* Information messages from the validator are also displayed in the output (if there were no errors/warnings these were previously suppressed)
* Scan Dependency packages for extensions!

### 20 November 2023
* Update FHIRPath expression validator and questionnaire validator project references
* Update unit tests to check the expected issue counts from each of the tested IGs

### 6 November 2023
* Processing dependencies now knows the resource type of the canonical to check against
* Questionnaires now processed in dependency scan
* Questionnaire is now included as one of the default resource types
* Support selecting individual files for importing `-sf package/SearchParameter-valueset-extensions-ValueSet-end.json` (when used, only selected files are processed)
* When processing dependent canonicals correctly handle the case where the canonical is to a contained resource

### 26 October 2023
* Produce a summary output of the resources that this IG directly has dependencies on (likely from dependent packages)
* output the above dependencies summary to a text file via a new -odf or --outputDependenciesFile commandline parameter
* Dependent resource scan now processes StructureMap and Questionnaire (in addition to StructureDefinition and ValueSet)

### 24 October 2023
* Remove restriction skipping expressions with `descendants()` usage

### 10 October 2023
* Update to the 5.3.0 Firely SDK
* FHIRPath expression validator updated, many false issues with search parameters now resolved, and support for `descendants()` function.
* collection based errors are now downgraded to warnings.

### 9 October 2023
* Include a summary count of each resource type in the package

### 21 September 2023
* Support multiple FHIR Versions R4, R4B, R5
* Verify the destination server version is compatible with the package version
* Additional error handling

### 20 September 2023
* Bug fix - null reference exceptions
* Add ".schema.json" files to the SkipFile routine so they aren't attempted to be read as FHIR json resources, and also skip non xml.json content (such as images)

### 18 August 2023
* Change the default value for the forceDownload to be true

### 11 August 2023
* Package Dependencies are displayed in the output report
* Canonical Resource dependencies are checked if they exist in the destination server
* Example filenames that are skipped are reported in verbose mode
* Canonical resource ID in the package is now ignored, will resolve by canonical URL/Version and create/update accordingly
* If multiple resources with the same canonical URL/version are in the destination server, resource is skipped 
