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
        public bool ForceDownload { get; set; }

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
        /// Check and clean any narratives in the package and remove suspect ones (based on the MS FHIR Server's rules)
        /// </summary>
        public bool CheckAndCleanNarratives { get; set; }

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
    }

    //public static class RazorViewToStringRendererFactory
    //{
    //    public static RazorViewToStringRenderer CreateRenderer()
    //    {

    //        var services = new ServiceCollection();
    //        services.AddSingleton<IHostingEnvironment>(new HostingEnvironment
    //        {
    //            ApplicationName = Assembly.GetEntryAssembly().GetName().Name
    //        });
    //        services.Configure<RazorViewEngineOptions>(options =>
    //        {
    //            options.FileProviders.Clear();
    //            options.FileProviders.Add(new PhysicalFileProvider(Directory.GetCurrentDirectory()));
    //        });
    //        services.AddSingleton<ObjectPoolProvider, DefaultObjectPoolProvider>();
    //        services.AddSingleton<DiagnosticSource>(new DiagnosticListener("Microsoft.AspNetCore"));
    //        services.AddLogging();
    //        var builder = services.AddMvcCore();
    //        builder.AddRazorPages();
    //        services.AddSingleton<RazorViewToStringRenderer>();
    //        var provider = services.BuildServiceProvider();
    //        return provider.GetRequiredService<RazorViewToStringRenderer>();
    //    }
    //}

    //public class RazorViewToStringRenderer
    //{
    //    private readonly IRazorViewEngine _viewEngine;
    //    private readonly ITempDataProvider _tempDataProvider;
    //    private readonly IServiceProvider _serviceProvider;

    //    public RazorViewToStringRenderer(
    //        IRazorViewEngine viewEngine,
    //        ITempDataProvider tempDataProvider,
    //        IServiceProvider serviceProvider)
    //    {
    //        _viewEngine = viewEngine;
    //        _tempDataProvider = tempDataProvider;
    //        _serviceProvider = serviceProvider;
    //    }

    //    public async Task<string> RenderViewToStringAsync<TModel>(string viewName, TModel model)
    //    {
    //        return await RenderViewToStringAsync(viewName, model, new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()));
    //    }

    //    public async Task<string> RenderViewToStringAsync<TModel>(string viewName, TModel model, ViewDataDictionary viewDataDictionary)
    //    {
    //        var actionContext = GetActionContext();
    //        var view = FindView(actionContext, viewName);

    //        using (var output = new StringWriter())
    //        {
    //            var viewContext = new ViewContext(
    //                actionContext,
    //                view,
    //                new ViewDataDictionary<TModel>(viewDataDictionary, model),
    //                new TempDataDictionary(
    //                    actionContext.HttpContext,
    //                    _tempDataProvider),
    //                output,
    //                new HtmlHelperOptions());

    //            await view.RenderAsync(viewContext);

    //            return output.ToString();
    //        }
    //    }

    //    private IView FindView(ActionContext actionContext, string viewName)
    //    {
    //        var getViewResult = _viewEngine.GetView(executingFilePath: null, viewPath: viewName, isMainPage: true);
    //        if (getViewResult.Success)
    //        {
    //            return getViewResult.View;
    //        }

    //        var findViewResult = _viewEngine.FindView(actionContext, viewName, isMainPage: true);
    //        if (findViewResult.Success)
    //        {
    //            return findViewResult.View;
    //        }

    //        var searchedLocations = getViewResult.SearchedLocations.Concat(findViewResult.SearchedLocations);
    //        var errorMessage = string.Join(
    //            Environment.NewLine,
    //            new[] { $"Unable to find view '{viewName}'. The following locations were searched:" }.Concat(searchedLocations)); ;

    //        throw new InvalidOperationException(errorMessage);
    //    }

    //    private ActionContext GetActionContext()
    //    {
    //        var httpContext = new DefaultHttpContext
    //        {
    //            RequestServices = _serviceProvider
    //        };
    //        return new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
    //    }
    //}
}