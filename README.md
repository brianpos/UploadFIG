# UploadFIG - FHIR Implementation Guide (FIG) Uploader R4/R4B/R5
![UploadFIG logo](logo_small.png "UploadFIG logo")

This tool provides a way to deploy a FHIR Implementation Guide to a FHIR Server.<br/>
The content can be loaded from:
* (-pid) the fhir registry via packageID
* (-s) an explicit web location `https://build.fhir.org/ig/HL7/sdc/package.tgz`
* (-s) a package file on the local filesystem `c:\temp\package.tgz`
* (-s) a file wildcard pattern on the local filesystem `c:\temp\samples\*.json` *(needs `-fv R4`)*

During the upload step the utility will:
* GET the resource ID directly
    * compare if the resource has changed (excluding meta.versionId, meta.lastUpdated and text)
    * skip if the resource is the same
* search for the resource by canonical URL (if it is a canonical resource)
    * verify that there is not another resource on the server already using that canonical URL<br/>
      (hence uploading may cause issues resolving - *can be disabled via `-pdv false`*
    * verify that the version hasn't been messed with

During the processing this utility will:
* Validate any fhirpath invariants in profiles
* Validate any search parameters included
(Note: These validation results should be verified as correct and investigate if they would impact the operation of the guide in your environment/toolchain)
* Validate Questionnaires
* Several other IG related validations/consistency checks


## Running the utility

The utility is a dotnet tool and can be run from the command line using the `UploadFIG` command.
``` ps
> UploadFIG -pid hl7.fhir.au.base -pv 4.0.0 -d https://localhost:44348
      -cn -df json -dh "Authorization:Bearer ******"
      --includeReferencedDependencies
      -reg https://api.healthterminologies.gov.au/integration/R4/fhir -rego au-registry-content.json
      -ets https://tx.dev.hl7.org.au/fhir
      -of output-bundle.json
```
See the [examples](examples.md) page for additional examples.<br/>
See the [parameters](Parameters.md) page for the complete list of command line parameters.

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
3. Load in all the resources in all the dependencies *(not supported)*

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


---

## Change history

### 28 March 2025
* Minor bug fixes from testing
* Issue [#9](https://github.com/brianpos/UploadFIG/issues/9) Validate and set the json/xml format supported by the server automatically.
* Reformat the summary output collapsing sections together and focusing on the total output, rather than just the root package.
* Fix some minor discrepancies in the reporting of un-resolved canonicals related to registry sourced content

### 20 March 2025
* Added support for deploying/testing content directly from a folder via a pattern.
  e.g. `UploadFIG -t -s C:\git\hl7\sdc\input\resources\*.json -fv R4`
  *Internally this creates a temporary package and then processes it as though it was a package.*

### 19 March 2025
* Issue [#21](https://github.com/brianpos/UploadFIG/issues/21) Resource Types `*` to not filter out any types (default is a subset of canonicals)
* Better handling of tree shaking dependent resources that aren't scoped in (as newer versions are already in scope)
* Don't test the types in a logical model as they aren't FHIR types.
* Fixed issue [#24](https://github.com/brianpos/UploadFIG/issues/24) Obscure error when package doesn't define the FHIR version
* Report an error if the package dependency can't be loaded (e.g. requesting `current` version)

### 6 March 2025
* Add the `-ap` or `--AdditionalPackages` flag to include additional packages in the processing<br/>
  *These will be processed as though they are dependencies of the root package.*
  *This is useful if you want to include additional packages that are not dependencies of the root package*
  *Particularly to resolve resources from a newer terminology.hl7.org or hl7.fhir.uv.extensions package version*

### 5 March 2025
This has been a big release with lots of changes, mostly around processing dependencies

* References both fhir package registries to resolve dependencies (https://packages.simplifier.net and https://packages2.fhir.org/packages)
* Added `Library` to the default resource types to process
* Added `-sn` or `--stripNarratives` flag to remove all narratives from the resources processed
* Added `-rms` or `--removeSnapshots` flag to remove all snapshots from StructureDefinitions
* Added `-pcv` or `--patchCanonicalVersions` flag to patch canonical URL references to be version specific if they resolve within the package or its dependencies
* Added `-mes` or `--maxExpansionSize` flag to set the maximum number of codes to include in a ValueSet expansion
* Added `-of` or `--outputBundle` filename to write a json batch bundle containing all of the processed resources
* Added `-rms` or `--removeSnapshots` flag to remove all snapshots from StructureDefinitions, this is different to `-rs` which regenerates them.
  *This is useful if the target server regenerates its own snapshots on submission.*
* The processing of package dependencies now more accurately reflects the actual resources that are required by the package,
  and canonical versioning considers the actual version of the resource that is required by the package based on context.
* Added `-ip` or `--ignorePackages` flag to ignore specific versioned packages when processing dependencies
  *This is really useful if wanting to exclude specific versions of a package where multiple are available*
  *e.g. only use us-core 6.1.0, and not 7.0.0 or 3.1.1*

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
