// See https://aka.ms/new-console-template for more information
using Firely.Fhir.Packages;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Utility;
using Newtonsoft.Json;
using SharpCompress.Readers;
using System.Diagnostics;
using System.Text;

Console.WriteLine("HL7 FHIR Implementation Guide Uploader");
Console.WriteLine("--------------------------------------");

// Server address to upload the content to (and check for consistency)
string fhirServerAddress = "https://localhost:44391/";

// package ID and version (for reading from a registry)
string fhirPackageId = "hl7.fhir.au.base";
string fhirPackageVersion = "4.0.0";

// Direct path to a package source (for direct download approach)
string fhirPackageSource = "https://hl7.org.au/fhir/4.0.0/package.tgz";

var importResourceTypes = new[] {
    "StructureDefinition",
    "ValueSet",
    "CodeSystem",
    "SearchParameter",
    "ConceptMap",
    "StructureMap",
};

// Prepare a temp working folder to hold this downloaded package
string tempFIGpath = Path.Combine(Path.GetTempPath(), "FIG");
string localPackagePath = Path.Combine(tempFIGpath, "demo-upload.tgz");
if (!Directory.Exists(tempFIGpath))
{
    Directory.CreateDirectory(tempFIGpath);
}

// Server to upload the resources to
FhirClient clientFhir = new FhirClient(fhirServerAddress);
// clientFhir.Settings.PreferredFormat = Hl7.Fhir.Rest.ResourceFormat.Xml;


// Check with the registry (for all versions of the package)
PackageClient pc = PackageClient.Create();
var examplesPkg = await pc.GetPackage(new PackageReference(fhirPackageId, null));
string contents = Encoding.UTF8.GetString(examplesPkg);
var pl = JsonConvert.DeserializeObject<PackageListing>(contents);
Console.WriteLine($"{pl?.Name} {String.Join(", ", pl.Versions.Keys)}");
if (!pl.Versions.ContainsKey(fhirPackageVersion))
{
    Console.Error.WriteLine($"Version {fhirPackageVersion} was not in the registered versions");
    return;
}
Console.WriteLine($"Package is for FHIR version: {pl.Versions[fhirPackageVersion].FhirVersion}");
Console.WriteLine($"Canonical URL: {pl.Versions[fhirPackageVersion].Url}");
Console.WriteLine($"{pl.Versions[fhirPackageVersion].Description}");
Console.WriteLine($"Direct location: {pl.Versions[fhirPackageVersion].Dist?.Tarball}");

// Download the file from the HL7 registry/or other location
Stream sourceStream;
if (!System.IO.File.Exists(localPackagePath))
{
    Console.WriteLine($"Downloading to {localPackagePath}");

    // Firely Package Manager approach (this will download into the users profile .fhir folder)
    // var pr = new Firely.Fhir.Packages.PackageReference(fhirPackageId, fhirPackageVersion);
    // examplesPkg = await pc.GetPackage(pr);

    // Direct download approach
    HttpClient client = new HttpClient();
    examplesPkg = await client.GetByteArrayAsync(fhirPackageSource);
    System.IO.File.WriteAllBytes(localPackagePath, examplesPkg);
    sourceStream = new MemoryStream(examplesPkg);
}
else
{
    // Local package was already downloaded
    Console.WriteLine($"Reading {localPackagePath}");
    sourceStream = System.IO.File.OpenRead(localPackagePath);
}


Stream gzipStream = new System.IO.Compression.GZipStream(sourceStream, System.IO.Compression.CompressionMode.Decompress);
MemoryStream ms = new MemoryStream();
using (gzipStream)
{
    // Unzip the tar file into a memory stream
    await gzipStream.CopyToAsync(ms);
    ms.Seek(0, SeekOrigin.Begin);
}
var reader = ReaderFactory.Open(ms);

long successes = 0;
long failures = 0;
var sw = Stopwatch.StartNew();


// disable validation during parsing (not its job)
var jsparser = new FhirJsonParser(new ParserSettings() { AcceptUnknownMembers = true, AllowUnrecognizedEnums = true, PermissiveParsing = true });
var xmlParser = new FhirXmlParser();

var errs = new List<String>();
var errFiles = new List<String>();

while (reader.MoveToNextEntry())
{
    if (SkipFile(reader.Entry.Key))
        continue;
    if (!reader.Entry.IsDirectory)
    {
        var exampleName = reader.Entry.Key;
        var stream = reader.OpenEntryStream();
        using (stream)
        {
            Resource resource;
            try
            {
                if (exampleName.EndsWith(".xml"))
                {
                    using (var xr = SerializationUtil.XmlReaderFromStream(stream))
                    {
                        resource = xmlParser.Parse<Resource>(xr);
                    }
                }
                else
                {
                    using (var jr = SerializationUtil.JsonReaderFromStream(stream))
                    {
                        resource = jsparser.Parse<Resource>(jr);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: ({exampleName}) {ex.Message}");
                System.Threading.Interlocked.Increment(ref failures);
                if (!errs.Contains(ex.Message))
                    errs.Add(ex.Message);
                errFiles.Add(exampleName);
                continue;
            }

            // Skip resource types we're not intentionally importing
            // (usually examples)
            if (!importResourceTypes.Contains(resource.TypeName))
                continue;

            try
            {
                if (resource is SearchParameter sp)
                {
                    if (!sp.Base.Any())
                    {
                        // Quietly skip them
                        Console.Error.WriteLine($"ERROR: ({exampleName}) Search parameter with no base");
                        System.Threading.Interlocked.Increment(ref failures);
                        // DebugDumpOutputXml(resource);
                        errFiles.Add(exampleName);
                        continue;
                    }
                }

                Resource result = UploadFile(clientFhir, resource);
                if (result != null)
                    System.Threading.Interlocked.Increment(ref successes);
                else
                    System.Threading.Interlocked.Increment(ref failures);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: ({exampleName}) {ex.Message}");
                System.Threading.Interlocked.Increment(ref failures);
                // DebugDumpOutputXml(resource);
                errFiles.Add(exampleName);
            }
        }
    }
}

sw.Stop();
Console.WriteLine("Done!");
Console.WriteLine();

if (errs.Any() || errFiles.Any())
{
    Console.WriteLine("-----------------------------------");
    Console.WriteLine(String.Join("\r\n", errs));
    Console.WriteLine("-----------------------------------");
    Console.WriteLine(String.Join("\r\n", errFiles));
    Console.WriteLine("-----------------------------------");
}
Console.WriteLine($"Success: {successes}");
Console.WriteLine($"Failures: {failures}");
Console.WriteLine($"Duration: {sw.Elapsed.ToString()}");
Console.WriteLine($"rps: {(successes + failures) / sw.Elapsed.TotalSeconds}");



bool SkipFile(string filename)
{
    // Schematron files typically included in the package are not wanted
    if (filename.EndsWith(".sch"))
        return true;

    // The package index file isn't to be uploaded
    if (filename.EndsWith("package.json"))
        return true;
    if (filename.EndsWith(".index.json"))
        return true;

    // Other internal Package files aren't to be considered either
    if (filename.EndsWith("spec.internals"))
        return true;
    if (filename.EndsWith("validation-summary.json"))
        return true;
    if (filename.EndsWith("validation-oo.json"))
        return true;

    return false;
}

Resource? UploadFile(FhirClient clientFhir, Resource resource)
{
    // Check to see if the resource is the same on the server already
    // (except for text/version/modified)
    var oldColor = Console.ForegroundColor;
    try
    {
        // reset properties that are set on the server anyway
        if (resource.Meta == null) resource.Meta = new Meta();
        resource.Meta.LastUpdated = null;
        resource.Meta.VersionId = null;

        Resource? current = null;
        if (!string.IsNullOrEmpty(resource.Id))
            current = clientFhir.Get($"{resource.TypeName}/{resource.Id}");
        if (current != null)
        {
            Resource original = (Resource)current.DeepCopy();
            current.Meta.LastUpdated = null;
            current.Meta.VersionId = null;
            if (current is DomainResource dr)
                dr.Text = (resource as DomainResource)?.Text;
            if (current.IsExactly(resource))
            {
                Console.WriteLine($"    {resource.TypeName}/{resource.Id} {resource.VersionId} unchanged");
                return original;
            }
        }

        if (resource is IVersionableConformanceResource vcs)
        {
            // Also search to see if there is another canonical version of this instance that would clash with it
            var others = clientFhir.Search(resource.TypeName, new[] { $"url={vcs.Url}" });
            if (others.Entry.Count > 1)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"    {resource.TypeName}/{resource.Id} {resource.VersionId} error");
                Console.Error.WriteLine($"ERROR: Canonical {vcs.Url} already has multiple copies loaded");
                Console.ForegroundColor = oldColor;
                return null;
            }
            // And check that the one we're loading in has the same ID
            if (others.Entry.Count == 1)
            {
                var currentFound = others.Entry[0].Resource as IVersionableConformanceResource;
                if (others.Entry[0].Resource?.TypeName != resource.TypeName)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"    {resource.TypeName}/{resource.Id} {resource.VersionId} error");
                    Console.Error.WriteLine($"ERROR: Canonical {vcs.Url} returned a different type");
                    Console.ForegroundColor = oldColor;
                    return null;
                }
                if (currentFound.Version != vcs.Version)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"    {resource.TypeName}/{resource.Id} {resource.VersionId} error");
                    Console.Error.WriteLine($"ERROR: Canonical {vcs.Url} has version {currentFound.Version} already loaded, can't also load {vcs.Version}");
                    Console.ForegroundColor = oldColor;
                    return null;
                }
                if (string.IsNullOrEmpty(resource.Id))
                {
                    // Use the same resource ID
                    // (as was expecting to use the server assigned ID - don't expect to hit here as standard packaged resources have an ID from the IG publisher)
                    resource.Id = others.Entry[0].Resource.Id;
                }
                else if (others.Entry[0].Resource.Id != resource.Id)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"    {resource.TypeName}/{resource.Id} {resource.VersionId} error");
                    Console.Error.WriteLine($"ERROR: Canonical {vcs.Url} has id {others.Entry[0].Resource.Id} on the server, can't also load id {resource.Id}");
                    Console.ForegroundColor = oldColor;
                    return null;
                }
            }
        }

    }
    catch (FhirOperationException fex)
    {
        if (fex.Status != System.Net.HttpStatusCode.NotFound && fex.Status != System.Net.HttpStatusCode.Gone)
        {
            Console.Error.WriteLine($"Warning: {resource.TypeName}/{resource.Id} {fex.Message}");
        }
    }


    // Now that we've established that it is new/different, upload it
    Resource result;
    if (!string.IsNullOrEmpty(resource.Id))
        result = clientFhir.Update(resource);
    else
        result = clientFhir.Create(resource);

    Console.ForegroundColor = ConsoleColor.DarkGreen;
    Console.WriteLine($"    {resource.TypeName}/{resource.Id} {resource.VersionId} uploaded");
    Console.ForegroundColor = oldColor;
    return result;
}
