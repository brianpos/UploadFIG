# UploadFIG - Sample FHIR Implementation Guide (FIG) Uploader
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

## Using the project (change code before running)
At this stage to use this project you'll need to tweak a few values in the program.cs
Find the variables near the top and tweak to what you want to use.
(the packageId and version are there to see if they are in the fhir registry.
if the package is private, you can skip that part and just go direct to your own TGZ)

```c#
// Server address to upload the content to (and check for consistency)
string fhirServerAddress = "https://localhost:44391/";

// package ID and version (for reading from a registry)
string fhirPackageId = "hl7.fhir.au.base";
string fhirPackageVersion = "4.0.0";

// Direct path to a package source (for direct download approach)
string fhirPackageSource = "https://hl7.org.au/fhir/4.0.0/package.tgz";
```