# ![UploadFIG logo](logo_tiny.png "UploadFIG logo") UploadFIG - Usage Examples

## Review the SDOH Clinical Care IG Package
Test the package content and not try and upload any data to a server, and will grab the latest
version from the HL7 FHIR Package Registry
(and also validate any resource dependencies from package dependencies - US-Core/SDC...)
``` ps
> UploadFIG -pid hl7.fhir.us.sdoh-clinicalcare -t -vrd
```

## Verify an installation of the US Core v6.1.0
Check to see if the US Core IG Package v6.1.0 is loaded onto a local server, and if any content has changed
``` ps
> UploadFIG -pid hl7.fhir.us.core -pv 6.1.0 -c -d https://localhost:44348 --verbose
```

## Skip processing of a specific file
Using the `-if` *(or `--ignoreFile`)* flag
``` ps
> UploadFIG -d https://localhost:44348 -pid hl7.fhir.au.base -pv 4.0.0 -if package/StructureDefinition-medication-brand-name.json
```

## Direct download a specific package
(Note that you should include the forceDownload flag here to ensure that it doesn't use a locally saved file)
``` ps
> UploadFIG -d https://localhost:44348 -s https://example.org/demo-package.tgz --verbose --forceDownload
```
 
## Test a locally built package
``` ps
> UploadFIG -s E:\git\HL7\fhir-sdoh-clinicalcare-publish\output\package.r4b.tgz -t
```

## Upload AU Base to a Microsoft FHIR Server
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

## Upload the latest version of the SDC IG to a FHIR Server in JSON format
Some fhir servers may only be able to support a single format, so you can specify xml or json explicitly to use while uploading.
This is independent of the format of the content that is native inside the IG package.
``` ps
> UploadFIG -pid hl7.fhir.au.base -d https://localhost:44348 -df json
```

## Deploy the latest version of the Davinci CRD IG and dependent resource to a FHIR Server
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

## Deploy an IG using a batch bundle and curl
If you want to deploy the IG using a transaction bundle, you can use the `-of` flag to write the bundle to a file
and then use curl to upload the bundle to the server.
``` cmd
# download the HL7 Australia au-base IG package, extract all the resources into a 
# transaction bundle and write it to the file `transaction-bundle.json`
> UploadFIG -t -pid hl7.fhir.au.base -of transaction-bundle.json
```
Then use curl to upload the bundle to the server (and report the results to the file `result.json`)
``` cmd
> curl -X POST -H "Content-Type: application/fhir+json" --data @transaction-bundle.json https://localhost:44391/ -o result.json
```
> **Note:** that this transaction bundle uses conditional updates to select the ID of the resource to update for canonical resources.
> Other resources will just POST the resource to create a new instance.
> And updates all records, not just ones that are changed.

## Deploy the CRD IG with only US-Core 6.1.0 (not 7.0.0 or 3.1.1) into a bundle
And version pin all the canonical references (including the dependencies) to the specific version of the US-Core IG,
using the `-pcv` Patch Canonical Versions flag

``` cmd
# download the CI build CRD IG package, extract all the resources into a batch bundle and write it to the file `bundle.json`
> UploadFIG -t -s https://build.fhir.org/ig/HL7/davinci-crd/branches/master/package.tgz
            --includeReferencedDependencies
            -ip hl7.fhir.us.core|7.0.0
            -ip hl7.fhir.us.core|3.1.1
            -pcv
            -of bundle.json
```
