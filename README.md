# UploadFIG - FHIR Implementation Guide (FIG) Uploader R4B
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
                                                             [default: StructureDefinition|ValueSet|CodeSystem|SearchParameter|Library
                                                             |ConceptMap|StructureMap]
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
  --includeExamples                                          Also include files in the examples sub-directory
                                                             (Still needs resource type specified)
  --verbose                                                  Provide verbose diagnostic output while processing
                                                             (e.g. Filenames processed)
                                                             [default: False]
  --version                                                  Show version information
  -?, -h, --help                                             Show help and usage information
```

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

### Unistalling
``` ps
PS C:\Users\brian> dotnet tool uninstall uploadfig --global
Tool 'uploadfig' (version '2023.8.3.15') was successfully uninstalled.
```

## Understanding the output
### Pacakge Metadata
The first section in the output is the metadata about the package that was downloaded and is being processed.
It finishes with the list of package dependencies in the project.

### Scanning package content
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

### Pacakge Content Summary (Test mode only)
When run in TestMode the output will also include a table of all the canonical resources that it processed for reference.

``` txt
Package content summary:
        Canonical Url  Canonical Version       Status  Name
        http://hl7.org/fhir/us/davinci-ra/CodeSystem/coding-gap-annotation      2.0.0-ballot    Active  CodingGapAnnotation
        http://hl7.org/fhir/us/davinci-ra/CodeSystem/coding-gap-task-reason     2.0.0-ballot    Draft   CodingGapTaskReason
        http://hl7.org/fhir/us/davinci-ra/CodeSystem/evidence-status    2.0.0-ballot    Active  RiskAdjustmentEvidenceStatus
        http://hl7.org/fhir/us/davinci-ra/CodeSystem/hierarchical-status        2.0.0-ballot    Active  RiskAdjustmenthierarchicalStatus
        http://hl7.org/fhir/us/davinci-ra/CodeSystem/suspect-type       2.0.0-ballot    Active  RiskAdjustmentSuspectType
```

### Dependency Verification
This section displays a summary of all the resource depencencies that were detected as required
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
``` ps
> UploadFIG -pid hl7.fhir.us.sdoh-clinicalcare  -t
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
> UploadFIG -d https://localhost:44348 -pid hl7.fhir.au.base -pv 4.0.0 -cn
```

### Upload the latest version of the SDC IG to a FHIR Server in JSON format
Some fhir servers may only be able to support a single format, so you can specify xml or json explicitly to use while uploading.
This is independent of the format of the content that is native inside the IG package.
``` ps
> UploadFIG -pid hl7.fhir.au.base -d https://localhost:44348 -df json
```

## Change history

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
