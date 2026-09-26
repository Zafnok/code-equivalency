# Runtime-changes review

Ticket M2-007 (ADR 0024, ADR 0035). Every behavioural entry on Microsoft's compatibility pages for
the .NET Framework to .NET path is below. Each one either becomes a row in
`src/Equiv.Core/RuntimeChanges/runtime-changes.json` (`source: documented`) or is excluded for one
of the reasons listed here. `RuntimeChangeTableTests.ReviewFileAndTableAgree` checks that every
`row:` prefix exists in the table, that every table row appears here, and that every exclusion
uses one of the allowed reasons.

Pages reviewed (dotnet/docs, `docs/core/compatibility/`, as of 2026-09-24): the breaking-change
indexes for .NET Core 3.0 and 3.1 and .NET 5 through .NET 10, the .NET Framework to .NET Core page
(`fx-core`), and the list of APIs that always throw on .NET (`unsupported-apis`), which the porting
page links. Sections that are build-time or deployment by nature (SDK, MSBuild, deployment,
containers, install tool, code analysis) are left out, as are the ASP.NET Core and EF Core pages
from .NET 5 on, which live outside `dotnet/core/compatibility/`.

## Format

One line per entry: `- <title> — <url> — row: <prefix>[ ; <prefix>...]` or
`- <title> — <url> — excluded: <reason>`. A prefix is matched against a call's identity
(`Namespace.Type::Member(ParamType,...)`), exactly as the table matches it.

## Allowed exclusion reasons

- `not a BCL member`: the change is not observable through a call to a specific member the .NET
  Framework base class library ships. This covers out-of-band packages (ASP.NET Core, EF Core,
  `Microsoft.Extensions.*`, `System.Text.Json`, `System.Diagnostics.DiagnosticSource`), whose
  behaviour follows the package version rather than the runtime; UI rendering, layout, designer
  and template changes; casts and conversions that are not calls; and entries that name no member.
- `build-time only`: the change shows up when compiling (obsoletion, nullable annotations,
  parameter renames, overload resolution, removed or moved APIs, project settings). No textually
  identical call behaves differently at run time.
- `not reachable from .NET Framework code`: the behaviour exists only where .NET Framework code
  never runs (Linux, macOS, browser, MAUI, APIs that .NET Framework does not have), or the change
  is between two .NET versions and .NET 10 behaves as .NET Framework 4.8 does.
- `configuration only`: the difference depends on configuration rather than code (app.config
  switches, environment variables, runtimeconfig or publish settings such as single-file,
  trimming, globalization-invariant mode or the high-DPI mode).
- `covered by row <prefix>`: another row already flags the affected calls.

## Entries

### .NET Core 3.0 — ASP.NET Core

- Obsolete Antiforgery, CORS, Diagnostics, MVC, and Routing APIs removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#obsolete-antiforgery-cors-diagnostics-mvc-and-routing-apis-removed — excluded: not a BCL member
- "Pubternal" APIs removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#pubternal-apis-removed — excluded: not a BCL member
- Authentication: Google+ deprecation — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#authentication-google-deprecated-and-replaced — excluded: not a BCL member
- Authentication: HttpContext.Authentication property removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#authentication-httpcontextauthentication-property-removed — excluded: not a BCL member
- Authentication: Newtonsoft.Json types replaced — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#authentication-newtonsoftjson-types-replaced — excluded: not a BCL member
- Authentication: OAuthHandler ExchangeCodeAsync signature changed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#authentication-oauthhandler-exchangecodeasync-signature-changed — excluded: not a BCL member
- Authorization: AddAuthorization overload moved to different assembly — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#authorization-addauthorization-overload-moved-to-different-assembly — excluded: not a BCL member
- Authorization: IAllowAnonymous removed from AuthorizationFilterContext.Filters — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#authorization-iallowanonymous-removed-from-authorizationfiltercontextfilters — excluded: not a BCL member
- Authorization: IAuthorizationPolicyProvider implementations require new method — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#authorization-iauthorizationpolicyprovider-implementations-require-new-method — excluded: not a BCL member
- Caching: CompactOnMemoryPressure property removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#caching-compactonmemorypressure-property-removed — excluded: not a BCL member
- Caching: Microsoft.Extensions.Caching.SqlServer uses new SqlClient package — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#caching-microsoftextensionscachingsqlserver-uses-new-sqlclient-package — excluded: not a BCL member
- Caching: ResponseCaching "pubternal" types changed to internal — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#caching-responsecaching-pubternal-types-changed-to-internal — excluded: not a BCL member
- Data Protection: DataProtection.Blobs uses new Azure Storage APIs — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#data-protection-dataprotectionblobs-uses-new-azure-storage-apis — excluded: not a BCL member
- Hosting: AspNetCoreModule V1 removed from Windows Hosting Bundle — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#hosting-aspnetcoremodule-v1-removed-from-windows-hosting-bundle — excluded: not a BCL member
- Hosting: Generic host restricts Startup constructor injection — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#hosting-generic-host-restricts-startup-constructor-injection — excluded: not a BCL member
- Hosting: HTTPS redirection enabled for IIS out-of-process apps — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#hosting-https-redirection-enabled-for-iis-out-of-process-apps — excluded: not a BCL member
- Hosting: IHostingEnvironment and IApplicationLifetime types replaced — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#hosting-ihostingenvironment-and-iapplicationlifetime-types-marked-obsolete-and-replaced — excluded: not a BCL member
- Hosting: ObjectPoolProvider removed from WebHostBuilder dependencies — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#hosting-objectpoolprovider-removed-from-webhostbuilder-dependencies — excluded: not a BCL member
- HTTP: DefaultHttpContext extensibility removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#http-defaulthttpcontext-extensibility-removed — excluded: not a BCL member
- HTTP: HeaderNames fields changed to static readonly — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#http-headernames-constants-changed-to-static-readonly — excluded: not a BCL member
- HTTP: Response body infrastructure changes — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#http-response-body-infrastructure-changes — excluded: not a BCL member
- HTTP: Some cookie SameSite default values changed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#http-some-cookie-samesite-defaults-changed-to-none — excluded: not a BCL member
- HTTP: Synchronous IO disabled by default — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#http-synchronous-io-disabled-in-all-servers — excluded: not a BCL member
- Identity: AddDefaultUI method overload removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#identity-adddefaultui-method-overload-removed — excluded: not a BCL member
- Identity: UI Bootstrap version change — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#identity-default-bootstrap-version-of-ui-changed — excluded: not a BCL member
- Identity: SignInAsync throws exception for unauthenticated identity — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#identity-signinasync-throws-exception-for-unauthenticated-identity — excluded: not a BCL member
- Identity: SignInManager constructor accepts new parameter — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#identity-signinmanager-constructor-accepts-new-parameter — excluded: not a BCL member
- Identity: UI uses static web assets feature — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#identity-ui-uses-static-web-assets-feature — excluded: not a BCL member
- Kestrel: Connection adapters removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#kestrel-connection-adapters-removed — excluded: not a BCL member
- Kestrel: Empty HTTPS assembly removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#kestrel-empty-https-assembly-removed — excluded: not a BCL member
- Kestrel: Request trailer headers moved to new collection — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#kestrel-request-trailer-headers-moved-to-new-collection — excluded: not a BCL member
- Kestrel: Transport abstraction layer changes — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#kestrel-transport-abstractions-removed-and-made-public — excluded: not a BCL member
- Localization: APIs marked obsolete — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#localization-resourcemanagerwithculturestringlocalizer-and-withculture-marked-obsolete — excluded: not a BCL member
- Logging: DebugLogger class made internal — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#logging-debuglogger-class-made-internal — excluded: not a BCL member
- MVC: Controller action Async suffix removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#mvc-async-suffix-trimmed-from-controller-action-names — excluded: not a BCL member
- MVC: JsonResult moved to Microsoft.AspNetCore.Mvc.Core — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#mvc-jsonresult-moved-to-microsoftaspnetcoremvccore — excluded: not a BCL member
- MVC: Precompilation tool deprecated — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#mvc-precompilation-tool-deprecated — excluded: not a BCL member
- MVC: Types changed to internal — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#mvc-pubternal-types-changed-to-internal — excluded: not a BCL member
- MVC: Web API compatibility shim removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#mvc-web-api-compatibility-shim-removed — excluded: not a BCL member
- Razor: RazorTemplateEngine API removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#razor-razortemplateengine-api-removed — excluded: not a BCL member
- Razor: Runtime compilation moved to a package — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#razor-runtime-compilation-moved-to-a-package — excluded: not a BCL member
- Session state: Obsolete APIs removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#session-state-obsolete-apis-removed — excluded: not a BCL member
- Shared framework: Assembly removal from Microsoft.AspNetCore.App — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#shared-framework-assemblies-removed-from-microsoftaspnetcoreapp — excluded: not a BCL member
- Shared framework: Microsoft.AspNetCore.All removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#shared-framework-removed-microsoftaspnetcoreall — excluded: not a BCL member
- SignalR: HandshakeProtocol.SuccessHandshakeData replaced — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#signalr-handshakeprotocolsuccesshandshakedata-replaced — excluded: not a BCL member
- SignalR: HubConnection methods removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#signalr-hubconnection-resetsendping-and-resettimeout-methods-removed — excluded: not a BCL member
- SignalR: HubConnectionContext constructors changed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#signalr-hubconnectioncontext-constructors-changed — excluded: not a BCL member
- SignalR: JavaScript client package name change — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#signalr-javascript-client-package-name-changed — excluded: not a BCL member
- SignalR: Obsolete APIs — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#signalr-usesignalr-and-useconnections-methods-marked-obsolete — excluded: not a BCL member
- SPAs: SpaServices and NodeServices marked obsolete — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#spas-spaservices-and-nodeservices-marked-obsolete — excluded: not a BCL member
- SPAs: SpaServices and NodeServices console logger fallback default change — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#spas-spaservices-and-nodeservices-no-longer-fall-back-to-console-logger — excluded: not a BCL member
- Target framework: .NET Framework not supported — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#target-framework-net-framework-support-dropped — excluded: not a BCL member

### .NET Core 3.0 — Core .NET libraries

- APIs that report version now report product and not file version — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#apis-that-report-version-now-report-product-and-not-file-version — row: System.Environment::get_Version( ; System.Runtime.InteropServices.RuntimeInformation::get_FrameworkDescription(
- Custom EncoderFallbackBuffer instances cannot fall back recursively — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#custom-encoderfallbackbuffer-instances-cannot-fall-back-recursively — row: System.Text.EncoderFallbackBuffer::Fallback( ; System.Text.EncoderFallbackBuffer::GetNextChar(
- Floating point formatting and parsing behavior changes — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#floating-point-formatting-and-parsing-behavior-changed — row: System.Double::ToString( ; System.Single::ToString( ; System.Double::Parse( ; System.Double::TryParse( ; System.Single::Parse( ; System.Single::TryParse(
- Floating-point parsing operations no longer fail or throw an OverflowException — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#floating-point-parsing-operations-no-longer-fail-or-throw-an-overflowexception — excluded: covered by row System.Double::Parse(
- InvalidAsynchronousStateException moved to another assembly — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#invalidasynchronousstateexception-moved-to-another-assembly — excluded: build-time only
- Replacing ill-formed UTF-8 byte sequences follows Unicode guidelines — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#replacing-ill-formed-utf-8-byte-sequences-follows-unicode-guidelines — row: System.Text.UTF8Encoding::GetCharCount( ; System.Text.UTF8Encoding::GetChars( ; System.Text.UTF8Encoding::GetString( ; System.Text.Encoding::GetCharCount( ; System.Text.Encoding::GetChars( ; System.Text.Encoding::GetString(
- TypeDescriptionProviderAttribute moved to another assembly — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#typedescriptionproviderattribute-moved-to-another-assembly — excluded: build-time only
- ZipArchiveEntry no longer handles archives with inconsistent entry sizes — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#ziparchiveentry-no-longer-handles-archives-with-inconsistent-entry-sizes — row: System.IO.Compression.ZipArchiveEntry::Open( ; System.IO.Compression.ZipFileExtensions::ExtractTo ; System.IO.Compression.ZipFile::ExtractToDirectory(
- FieldInfo.SetValue throws exception for static, init-only fields — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#fieldinfosetvalue-throws-exception-for-static-init-only-fields — row: System.Reflection.FieldInfo::SetValue(
- Passing GroupCollection to extension methods taking IEnumerable<T> requires disambiguation — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#passing-groupcollection-to-extension-methods-taking-ienumerablet-requires-disambiguation — excluded: build-time only

### .NET Core 3.0 — Cryptography

- BEGIN TRUSTED CERTIFICATE syntax no longer supported on Linux — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#begin-trusted-certificate-syntax-no-longer-supported-for-root-certificates-on-linux — excluded: not reachable from .NET Framework code
- EnvelopedCms defaults to AES-256 encryption — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#envelopedcms-defaults-to-aes-256-encryption — row: System.Security.Cryptography.Pkcs.EnvelopedCms::.ctor(
- Minimum size for RSAOpenSsl key generation has increased — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#minimum-size-for-rsaopenssl-key-generation-has-increased — excluded: not reachable from .NET Framework code
- .NET Core 3.0 prefers OpenSSL 1.1.x to OpenSSL 1.0.x — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#net-core-30-prefers-openssl-11x-to-openssl-10x — excluded: not reachable from .NET Framework code
- CryptoStream.Dispose transforms final block only when writing — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#cryptostreamdispose-transforms-final-block-only-when-writing — row: System.Security.Cryptography.CryptoStream::Dispose( ; System.Security.Cryptography.CryptoStream::Close(

### .NET Core 3.0 — Globalization

- "C" locale maps to the invariant locale — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#c-locale-maps-to-the-invariant-locale — excluded: not reachable from .NET Framework code

### .NET Core 3.0 — Networking

- Default value of HttpRequestMessage.Version changed to 1.1 — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#default-value-of-httprequestmessageversion-changed-to-11 — excluded: not reachable from .NET Framework code

### .NET Core 3.0 — Windows Forms

- Control.DefaultFont changed to Segoe UI 9 pt — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#default-control-font-changed-to-segoe-ui-9-pt — row: System.Windows.Forms.Control::get_DefaultFont(
- Modernization of the FolderBrowserDialog — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#modernization-of-the-folderbrowserdialog — excluded: not a BCL member
- SerializableAttribute removed from some Windows Forms types — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#serializableattribute-removed-from-some-windows-forms-types — excluded: covered by row System.Runtime.Serialization.Formatters.Binary.BinaryFormatter::
- AllowUpdateChildControlIndexForTabControls compatibility switch not supported — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#allowupdatechildcontrolindexfortabcontrols-compatibility-switch-not-supported — excluded: configuration only
- DomainUpDown.UseLegacyScrolling compatibility switch not supported — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#domainupdownuselegacyscrolling-compatibility-switch-not-supported — excluded: configuration only
- DoNotLoadLatestRichEditControl compatibility switch not supported — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#donotloadlatestricheditcontrol-compatibility-switch-not-supported — excluded: configuration only
- DoNotSupportSelectAllShortcutInMultilineTextBox compatibility switch not supported — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#donotsupportselectallshortcutinmultilinetextbox-compatibility-switch-not-supported — excluded: configuration only
- DontSupportReentrantFilterMessage compatibility switch not supported — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#dontsupportreentrantfiltermessage-compatibility-switch-not-supported — excluded: configuration only
- EnableVisualStyleValidation compatibility switch not supported — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#enablevisualstylevalidation-compatibility-switch-not-supported — excluded: configuration only
- UseLegacyContextMenuStripSourceControlValue compatibility switch not supported — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#uselegacycontextmenustripsourcecontrolvalue-compatibility-switch-not-supported — excluded: configuration only
- UseLegacyImages compatibility switch not supported — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#uselegacyimages-compatibility-switch-not-supported — excluded: configuration only
- About and SplashScreen templates are broken for Visual Basic — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#about-and-splashscreen-templates-are-broken — excluded: not a BCL member

### .NET Core 3.0 — WPF

- Altered drag-and-drop behavior on text editors — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.0#altered-drag-and-drop-behavior-on-text-editors — excluded: not reachable from .NET Framework code

### .NET 5 — Core .NET libraries

- Assembly-related API changes for single-file publishing — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/assembly-api-behavior-changes-for-single-file-publish — excluded: configuration only
- BinaryFormatter serialization methods are obsolete — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/5.0/binaryformatter-serialization-obsolete — excluded: covered by row System.Runtime.Serialization.Formatters.Binary.BinaryFormatter::
- Code access security APIs are obsolete — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/code-access-security-apis-obsolete — excluded: build-time only
- CreateCounterSetInstance throws InvalidOperationException — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/createcountersetinstance-throws-invalidoperation — row: System.Diagnostics.PerformanceData.CounterSet::CreateCounterSetInstance(
- Default ActivityIdFormat is W3C — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/default-activityidformat-changed — excluded: not a BCL member
- Environment.OSVersion returns the correct version — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/environment-osversion-returns-correct-version — row: System.Environment::get_OSVersion(
- FrameworkDescription's value is .NET not .NET Core — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/frameworkdescription-returns-net-not-net-core — excluded: covered by row System.Runtime.InteropServices.RuntimeInformation::get_FrameworkDescription(
- GAC APIs are obsolete — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/global-assembly-cache-apis-obsolete — row: System.Reflection.Assembly::get_GlobalAssemblyCache(
- Hardware intrinsic IsSupported checks — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/hardware-instrinsics-issupported-checks — excluded: not reachable from .NET Framework code
- IntPtr and UIntPtr implement IFormattable — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/intptr-uintptr-implement-iformattable — excluded: build-time only
- LastIndexOf handles empty search strings — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/lastindexof-improved-handling-of-empty-values — excluded: covered by row System.String::LastIndexOf(
- URI paths with non-ASCII characters on Unix — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/non-ascii-chars-in-uri-parsed-correctly — excluded: not reachable from .NET Framework code
- API obsoletions with non-default diagnostic IDs — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/obsolete-apis-with-custom-diagnostics — excluded: build-time only
- Obsolete properties on ConsoleLoggerOptions — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/obsolete-consoleloggeroptions-properties — excluded: not a BCL member
- Complexity of LINQ OrderBy.First — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/orderby-firstordefault-complexity-increase — row: System.Linq.Enumerable::First( ; System.Linq.Enumerable::FirstOrDefault(
- OSPlatform attributes renamed or removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/os-platform-attributes-renamed — excluded: build-time only
- Microsoft.DotNet.PlatformAbstractions package removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/platformabstractions-package-removed — excluded: not a BCL member
- PrincipalPermissionAttribute is obsolete — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/principalpermissionattribute-obsolete — excluded: build-time only
- Parameter name changes from preview versions — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/reference-assembly-parameter-names-rc1 — excluded: build-time only
- Parameter name changes in reference assemblies — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/reference-assembly-parameter-names — excluded: build-time only
- Remoting APIs are obsolete — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/remoting-apis-obsolete — excluded: covered by row System.MarshalByRefObject::GetLifetimeService(
- Order of Activity.Tags list is reversed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/reverse-order-of-tags-in-activity-property — excluded: not a BCL member
- SSE and SSE2 comparison methods handle NaN — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/sse-comparegreaterthan-intrinsics — excluded: not reachable from .NET Framework code
- Thread.Abort is obsolete — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/thread-abort-obsolete — excluded: covered by row System.Threading.Thread::Abort(
- Uri recognition of UNC paths on Unix — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/unc-path-recognition-unix — excluded: not reachable from .NET Framework code
- UTF-7 code paths are obsolete — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/utf-7-code-paths-obsolete — excluded: build-time only
- Behavior change for Vector2.Lerp and Vector4.Lerp — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/vector-lerp-behavior-change — row: System.Numerics.Vector2::Lerp( ; System.Numerics.Vector4::Lerp(
- Vector<T> throws NotSupportedException — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/vectort-throws-notsupportedexception — row: System.Numerics.Vector`1::

### .NET 5 — Cryptography

- Cryptography APIs not supported on browser — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/5.0/cryptography-apis-not-supported-on-blazor-webassembly — excluded: not reachable from .NET Framework code
- Cryptography.Oid is init-only — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/5.0/cryptography-oid-init-only — row: System.Security.Cryptography.Oid::set_Value( ; System.Security.Cryptography.Oid::set_FriendlyName(
- Default TLS cipher suites on Linux — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/5.0/default-cipher-suites-for-tls-on-linux — excluded: not reachable from .NET Framework code
- Create() overloads on cryptographic abstractions are obsolete — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/5.0/instantiating-default-implementations-of-cryptographic-abstractions-not-supported — excluded: covered by row System.Security.Cryptography.HashAlgorithm::Create()
- Default FeedbackSize value changed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/5.0/tripledes-default-feedback-size-change — excluded: not reachable from .NET Framework code

### .NET 5 — Globalization

- Use ICU libraries on Windows — https://learn.microsoft.com/en-us/dotnet/core/compatibility/globalization/5.0/icu-globalization-api — row: System.String::IndexOf( ; System.String::LastIndexOf( ; System.String::StartsWith( ; System.String::EndsWith( ; System.String::Compare( ; System.String::CompareTo( ; System.Globalization.CompareInfo::
- StringInfo and TextElementEnumerator are UAX29-compliant — https://learn.microsoft.com/en-us/dotnet/core/compatibility/globalization/5.0/uax29-compliant-grapheme-enumeration — row: System.Globalization.StringInfo:: ; System.Globalization.TextElementEnumerator::
- Unicode category changed for Latin-1 characters — https://learn.microsoft.com/en-us/dotnet/core/compatibility/globalization/5.0/unicode-categories-for-latin1-chars — row: System.Char::GetUnicodeCategory( ; System.Char::IsLetter( ; System.Char::IsPunctuation( ; System.Char::IsSymbol( ; System.Char::IsLower(
- TextInfo.ListSeparator values changed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/globalization/5.0/listseparator-value-change — row: System.Globalization.TextInfo::get_ListSeparator(

### .NET 5 — Interop

- Support for WinRT is removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/5.0/built-in-support-for-winrt-removed — excluded: build-time only
- Casting RCW to InterfaceIsIInspectable throws exception — https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/5.0/casting-rcw-to-inspectable-interface-throws-exception — excluded: not a BCL member
- No A/W suffix probing on non-Windows platforms — https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/5.0/function-suffix-pinvoke — excluded: not reachable from .NET Framework code

### .NET 5 — Networking

- Cookie path handling conforms to RFC 6265 — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/5.0/cookie-path-conforms-to-rfc6265 — row: System.Net.CookieContainer:: ; System.Net.Cookie::
- LocalEndPoint is updated after calling SendToAsync — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/5.0/localendpoint-updated-on-sendtoasync — row: System.Net.Sockets.Socket::get_LocalEndPoint(
- MulticastOption.Group doesn't accept null — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/5.0/multicastoption-group-doesnt-accept-null — row: System.Net.Sockets.MulticastOption::set_Group(
- Streams allow successive Begin operations — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/5.0/negotiatestream-sslstream-dont-fail-on-successive-begin-calls — row: System.Net.Security.SslStream::BeginAuthenticateAs ; System.Net.Security.NegotiateStream::BeginAuthenticateAs
- WinHttpHandler removed from .NET runtime — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/5.0/winhttphandler-removed-from-runtime — excluded: build-time only

### .NET 5 — Serialization

- BinaryFormatter.Deserialize rewraps exceptions — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/5.0/binaryformatter-deserialize-rewraps-exceptions — excluded: covered by row System.Runtime.Serialization.Formatters.Binary.BinaryFormatter::
- JsonSerializer.Deserialize requires single-character string — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/5.0/deserializing-json-into-char-requires-single-character — excluded: not a BCL member
- ASP.NET Core apps deserialize quoted numbers — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/5.0/jsonserializer-allows-reading-numbers-as-strings — excluded: not a BCL member
- JsonSerializer.Serialize throws ArgumentNullException — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/5.0/jsonserializer-serialize-throws-argumentnullexception-for-null-type — excluded: not a BCL member
- Non-public, parameterless constructors not used for deserialization — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/5.0/non-public-parameterless-constructors-not-used-for-deserialization — excluded: not a BCL member
- Options are honored when serializing key-value pairs — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/5.0/options-honored-when-serializing-key-value-pairs — excluded: not a BCL member

### .NET 5 — Windows Forms

- Native code can't access Windows Forms objects — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/5.0/winforms-objects-not-accessible-from-native-code — excluded: not a BCL member
- OutputType set to WinExe — https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/5.0/automatically-infer-winexe-output-type — excluded: build-time only
- DataGridView doesn't reset custom fonts — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/5.0/datagridview-doesnt-reset-custom-font-settings — row: System.Windows.Forms.DataGridView::set_Font(
- Methods throw ArgumentException — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/5.0/invalid-args-cause-argumentexception — row: System.Windows.Forms.TabControl::GetToolTipText( ; System.Windows.Forms.DataFormats::GetFormat(string ; System.Windows.Forms.InputLanguageChangedEventArgs::.ctor(
- Methods throw ArgumentNullException — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/5.0/null-args-cause-argumentnullexception — row: System.Windows.Forms.Control.ControlCollection::.ctor( ; System.Windows.Forms.DataGridViewComboBoxEditingControl::ApplyCellStyleToEditingControl( ; System.Windows.Forms.ListBox.IntegerCollection:: ; System.Windows.Forms.ListBox.ObjectCollection::.ctor( ; System.Windows.Forms.ListBox.ObjectCollection::AddRange( ; System.Windows.Forms.ListBox.ObjectCollection::CopyTo( ; System.Windows.Forms.ListView.ListViewItemCollection::Find( ; System.Windows.Forms.ListView.SelectedIndexCollection::.ctor( ; System.Windows.Forms.RichTextBox::LoadFile( ; System.Windows.Forms.ScrollableControl::OnPaintBackground( ; System.Windows.Forms.TableLayoutControlCollection::.ctor( ; System.Windows.Forms.ToolStripRenderer::OnRender ; System.Windows.Forms.TreeNodeCollection::Find( ; System.Windows.Forms.VisualStyles.VisualStyleRenderer::.ctor(
- Properties throw ArgumentOutOfRangeException — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/5.0/invalid-args-cause-argumentoutofrangeexception — row: System.Windows.Forms.TreeNode::set_ImageIndex( ; System.Windows.Forms.TreeNode::set_SelectedImageIndex(
- TextFormatFlags.ModifyString is obsolete — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/5.0/modifystring-field-of-textformatflags-obsolete — excluded: build-time only
- DataGridView APIs throw InvalidOperationException — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/5.0/null-owner-causes-invalidoperationexception — row: System.Windows.Forms.DataGridViewButtonCell.DataGridViewButtonCellAccessibleObject:: ; System.Windows.Forms.DataGridViewCheckBoxCell.DataGridViewCheckBoxCellAccessibleObject:: ; System.Windows.Forms.DataGridViewColumnHeaderCell.DataGridViewColumnHeaderCellAccessibleObject:: ; System.Windows.Forms.DataGridViewImageCell.DataGridViewImageCellAccessibleObject:: ; System.Windows.Forms.DataGridViewLinkCell.DataGridViewLinkCellAccessibleObject::
- WinForms apps use Microsoft.NET.Sdk — https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/5.0/sdk-and-target-framework-change — excluded: build-time only
- Removed status bar controls — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/5.0/winforms-deprecated-controls — excluded: build-time only

### .NET 5 — WPF

- WPF apps use Microsoft.NET.Sdk — https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/5.0/sdk-and-target-framework-change — excluded: build-time only

### .NET 6 — Core .NET libraries

- API obsoletions with non-default diagnostic IDs — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/obsolete-apis-with-custom-diagnostics — excluded: build-time only
- Changes to nullable reference type annotations — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/nullable-ref-type-annotation-changes — excluded: build-time only
- Conditional string evaluation in Debug methods — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/debug-assert-conditional-evaluation — row: System.Diagnostics.Debug::Assert(bool,ref  ; System.Diagnostics.Debug::WriteIf(bool,ref  ; System.Diagnostics.Debug::WriteLineIf(bool,ref 
- Environment.ProcessorCount behavior on Windows — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/environment-processorcount-on-windows — row: System.Environment::get_ProcessorCount(
- EventSource callback behavior — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/eventsource-callback — row: System.Diagnostics.Tracing.EventSource::IsEnabled(
- File.Replace on Unix throws exceptions to match Windows — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/file-replace-exceptions-on-unix — excluded: not reachable from .NET Framework code
- FileStream locks files with shared lock on Unix — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/filestream-file-locks-unix — excluded: not reachable from .NET Framework code
- FileStream no longer synchronizes file offset with OS — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/filestream-doesnt-sync-offset-with-os — excluded: not a BCL member
- FileStream.Position updates after ReadAsync or WriteAsync completes — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/filestream-position-updates-after-readasync-writeasync-completion — row: System.IO.FileStream::get_Position(
- New diagnostic IDs for obsoleted APIs — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/diagnostic-id-change-for-obsoletions — excluded: build-time only
- New System.Linq.Queryable method overloads — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/additional-linq-queryable-method-overloads — excluded: build-time only
- Older framework versions dropped from package — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/older-framework-versions-dropped — excluded: build-time only
- Parameter names changed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/parameter-name-changes — excluded: build-time only
- Parameter names in Stream-derived types — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/parameters-renamed-on-stream-derived-types — excluded: build-time only
- Partial and zero-byte reads in DeflateStream, GZipStream, and CryptoStream — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/partial-byte-reads-in-streams — row: System.IO.Compression.DeflateStream::Read ; System.IO.Compression.DeflateStream::BeginRead( ; System.IO.Compression.GZipStream::Read ; System.IO.Compression.GZipStream::BeginRead( ; System.Security.Cryptography.CryptoStream::Read ; System.Security.Cryptography.CryptoStream::BeginRead(
- Set timestamp on read-only file on Windows — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/set-timestamp-readonly-file — row: System.IO.File::SetCreationTime ; System.IO.File::SetLastAccessTime ; System.IO.File::SetLastWriteTime
- Standard numeric format parsing precision — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/numeric-format-parsing-handles-higher-precision — row: System.Byte::ToString(string ; System.SByte::ToString(string ; System.Int16::ToString(string ; System.UInt16::ToString(string ; System.Int32::ToString(string ; System.UInt32::ToString(string ; System.Int64::ToString(string ; System.UInt64::ToString(string ; System.Decimal::ToString(string
- Static abstract members in interfaces — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/static-abstract-interface-methods — excluded: build-time only
- StringBuilder.Append overloads and evaluation order — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/stringbuilder-append-evaluation-order — row: System.Text.StringBuilder::Append(ref  ; System.Text.StringBuilder::AppendLine(ref 
- Strong-name APIs throw PlatformNotSupportedException — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/strong-name-signing-exceptions — row: System.Reflection.StrongNameKeyPair:: ; System.Reflection.AssemblyName::get_KeyPair( ; System.Reflection.AssemblyName::set_KeyPair(
- System.Drawing.Common only supported on Windows — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/system-drawing-common-windows-only — excluded: not reachable from .NET Framework code
- System.Security.SecurityContext is marked obsolete — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/securitycontext-obsolete — excluded: covered by row System.Security.SecurityContext::Capture(
- Task.FromResult may return singleton — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/task-fromresult-returns-singleton — row: System.Threading.Tasks.Task::FromResult(
- Unhandled exceptions from a BackgroundService — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/hosting-exception-handling — excluded: not a BCL member

### .NET 6 — Cryptography

- CreateEncryptor methods throw exception for incorrect feedback size — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/6.0/cfb-mode-feedback-size-exception — row: System.Security.Cryptography.AesCng::CreateEncryptor( ; System.Security.Cryptography.AesCng::CreateDecryptor( ; System.Security.Cryptography.TripleDESCng::CreateEncryptor( ; System.Security.Cryptography.TripleDESCng::CreateDecryptor(

### .NET 6 — Extensions

- AddProvider checks for non-null provider — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/6.0/addprovider-null-check — excluded: not a BCL member
- FileConfigurationProvider.Load throws InvalidDataException — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/6.0/filename-in-load-exception — excluded: not a BCL member
- Repeated XML elements include index — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/6.0/repeated-xml-elements — excluded: not a BCL member
- Resolving disposed ServiceProvider throws exception — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/6.0/service-provider-disposed — excluded: not a BCL member

### .NET 6 — Globalization

- Culture creation and case mapping in globalization-invariant mode — https://learn.microsoft.com/en-us/dotnet/core/compatibility/globalization/6.0/culture-creation-invariant-mode — excluded: configuration only

### .NET 6 — JIT compiler

- Coerce call arguments according to ECMA-335 — https://learn.microsoft.com/en-us/dotnet/core/compatibility/jit/6.0/coerce-call-arguments-ecma-335 — excluded: not a BCL member

### .NET 6 — Networking

- Port removed from SPN for Kerberos and Negotiate — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/6.0/httpclient-port-lookup — excluded: not reachable from .NET Framework code
- WebRequest, WebClient, and ServicePoint are obsolete — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/6.0/webrequest-deprecated — excluded: build-time only

### .NET 6 — Serialization

- DataContractSerializer retains sign when deserializing -0 — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/7.0/datacontractserializer-negative-sign — row: System.Runtime.Serialization.DataContractSerializer::ReadObject( ; System.Runtime.Serialization.Json.DataContractJsonSerializer::ReadObject( ; System.Runtime.Serialization.XmlObjectSerializer::ReadObject(
- Default serialization format for TimeSpan — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/6.0/timespan-serialization-format — excluded: not a BCL member
- IAsyncEnumerable serialization — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/6.0/iasyncenumerable-serialization — excluded: not a BCL member
- JSON source-generation API refactoring — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/6.0/json-source-gen-api-refactor — excluded: not a BCL member
- JsonNumberHandlingAttribute on collection properties — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/6.0/jsonnumberhandlingattribute-behavior — excluded: not a BCL member
- New JsonSerializer source generator overloads — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/6.0/jsonserializer-source-generator-overloads — excluded: not a BCL member

### .NET 6 — Windows Forms

- C# templates use application bootstrap — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/6.0/application-bootstrap — excluded: not a BCL member
- Selected TableLayoutSettings properties throw InvalidEnumArgumentException — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/6.0/tablelayoutsettings-apis-throw-invalidenumargumentexception — row: System.Windows.Forms.TableLayoutPanel::set_CellBorderStyle( ; System.Windows.Forms.TableLayoutPanel::set_GrowStyle(
- DataGridView-related APIs now throw InvalidOperationException — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/6.0/null-owner-causes-invalidoperationexception — row: System.Windows.Forms.DataGridViewTopLeftHeaderCell.DataGridViewTopLeftHeaderCellAccessibleObject::
- ListViewGroupCollection methods throw new InvalidOperationException — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/6.0/listview-invalidoperationexception — row: System.Windows.Forms.ListViewGroupCollection::
- NotifyIcon.Text maximum text length increased — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/6.0/notifyicon-text-max-text-length-increased — row: System.Windows.Forms.NotifyIcon::set_Text(
- ScaleControl called only when needed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/6.0/optimize-scalecontrol-calls — excluded: not a BCL member
- Some APIs throw ArgumentNullException — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/6.0/apis-throw-argumentnullexception — row: System.Windows.Forms.TreeNodeCollection::get_Item( ; System.Windows.Forms.DrawTreeNodeEventArgs::.ctor( ; System.Windows.Forms.DataGridViewRowStateChangedEventArgs::.ctor( ; System.Windows.Forms.DataGridViewColumnStateChangedEventArgs::.ctor(
- TreeNodeCollection.Item throws exception if node is assigned elsewhere — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/6.0/treenodecollection-item-throws-argumentexception — row: System.Windows.Forms.TreeNodeCollection::set_Item(

### .NET 6 — XML and XSLT

- XNodeReader.GetAttribute behavior for invalid index — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/xnodereader-getattribute — row: System.Xml.XmlReader::GetAttribute(int)

### .NET 7 — Core .NET libraries

- API obsoletions with default diagnostic ID — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/obsolete-apis-with-default-diagnostic — excluded: build-time only
- API obsoletions with non-default diagnostic IDs — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/obsolete-apis-with-custom-diagnostics — excluded: build-time only
- Asterisk no longer accepted for assembly name attributes — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/assembly-name-wildcard — row: System.Reflection.Assembly::Load(string ; System.Reflection.AssemblyName::.ctor(string
- BinaryFormatter serialization APIs produce compiler errors — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/7.0/binaryformatter-apis-produce-errors — excluded: covered by row System.Runtime.Serialization.Formatters.Binary.BinaryFormatter::
- BrotliStream no longer allows undefined CompressionLevel values — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/brotlistream-ctor — excluded: not reachable from .NET Framework code
- C++/CLI projects in Visual Studio — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/cpluspluscli-compiler-version — excluded: build-time only
- Changes to reflection invoke API exceptions — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/reflection-invoke-exceptions — row: System.Reflection.MethodBase::Invoke( ; System.Reflection.ConstructorInfo::Invoke(
- Collectible Assembly in non-collectible AssemblyLoadContext — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/collectible-assemblies — excluded: not reachable from .NET Framework code
- DateTime addition methods precision change — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/datetime-add-precision — row: System.DateTime::AddDays( ; System.DateTime::AddHours( ; System.DateTime::AddMilliseconds( ; System.DateTime::AddMinutes( ; System.DateTime::AddSeconds(
- Equals method behavior change for NaN — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/equals-nan — row: System.Numerics.Vector2::Equals( ; System.Numerics.Vector3::Equals( ; System.Numerics.Vector4::Equals( ; System.Numerics.Matrix3x2::Equals( ; System.Numerics.Matrix4x4::Equals( ; System.Numerics.Plane::Equals( ; System.Numerics.Quaternion::Equals( ; System.Numerics.Vector`1::Equals(
- EventSource callback behavior — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/eventsource-callback — excluded: covered by row System.Diagnostics.Tracing.EventSource::IsEnabled(
- Generic type constraint on PatternContext<T> — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/patterncontext-generic-constraint — excluded: build-time only
- Legacy FileStream strategy removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/filestream-compat-switch — excluded: configuration only
- Library support for older frameworks — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/old-framework-support — excluded: build-time only
- Maximum precision for numeric format strings — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/max-precision-numeric-format-strings — excluded: covered by row System.Int32::ToString(string
- Regex patterns with ranges corrected — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/regex-ranges — row: System.Text.RegularExpressions.Regex::
- SerializationFormat.Binary is obsolete — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/7.0/serializationformat-binary — excluded: build-time only
- System.Drawing.Common config switch removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/system-drawing — excluded: configuration only
- System.Runtime.CompilerServices.Unsafe NuGet package — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/unsafe-package — excluded: build-time only
- Time fields on symbolic links — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/symbolic-link-timestamps — row: System.IO.Directory::SetCreationTime ; System.IO.Directory::SetLastAccessTime ; System.IO.Directory::SetLastWriteTime ; System.IO.FileSystemInfo::set_CreationTime ; System.IO.FileSystemInfo::set_LastAccessTime ; System.IO.FileSystemInfo::set_LastWriteTime
- Tracking linked cache entries — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/memorycache-tracking — excluded: not a BCL member
- Validate CompressionLevel for BrotliStream — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/compressionlevel-validation — excluded: not reachable from .NET Framework code

### .NET 7 — Configuration

- System.diagnostics entry in app.config — https://learn.microsoft.com/en-us/dotnet/core/compatibility/configuration/7.0/diagnostics-config-section — excluded: configuration only

### .NET 7 — Cryptography

- Decrypting EnvelopedCms doesn't double unwrap — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/7.0/decrypt-envelopedcms — excluded: not reachable from .NET Framework code
- Dynamic X509ChainPolicy verification time — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/7.0/x509chainpolicy-verification-time — row: System.Security.Cryptography.X509Certificates.X509ChainPolicy::get_VerificationTime( ; System.Security.Cryptography.X509Certificates.X509Chain::Build(
- X500DistinguishedName parsing of friendly names — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/7.0/x500-distinguished-names — row: System.Security.Cryptography.X509Certificates.X500DistinguishedName::.ctor(

### .NET 7 — Extensions

- Binding config to dictionary extends values — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/7.0/config-bind-dictionary — excluded: not a BCL member
- ContentRootPath for apps launched by Windows Shell — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/7.0/contentrootpath-hosted-app — excluded: not a BCL member
- Environment variable prefixes — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/7.0/environment-variable-prefix — excluded: not a BCL member

### .NET 7 — Globalization

- Globalization APIs use ICU libraries on Windows Server — https://learn.microsoft.com/en-us/dotnet/core/compatibility/globalization/7.0/icu-globalization-api — excluded: covered by row System.Globalization.CompareInfo::

### .NET 7 — Interop

- RuntimeInformation.OSArchitecture under emulation — https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/7.0/osarchitecture-emulation — row: System.Runtime.InteropServices.RuntimeInformation::get_OSArchitecture(

### .NET 7 — .NET MAUI

- Constructors accept base interface instead of concrete type — https://learn.microsoft.com/en-us/dotnet/core/compatibility/maui/7.0/mauiwebviewnavigationdelegate-constructor — excluded: not reachable from .NET Framework code
- Flow direction helper methods removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/maui/7.0/flow-direction-apis-removed — excluded: not reachable from .NET Framework code
- New UpdateBackground parameter — https://learn.microsoft.com/en-us/dotnet/core/compatibility/maui/7.0/updatebackground-parameter — excluded: not reachable from .NET Framework code
- ScrollToRequest property renamed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/maui/7.0/scrolltorequest-property-rename — excluded: not reachable from .NET Framework code
- Some Windows APIs are removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/maui/7.0/iwindowstatemanager-apis-removed — excluded: not reachable from .NET Framework code

### .NET 7 — Networking

- AllowRenegotiation default is false — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/7.0/allowrenegotiation-default — row: System.Net.Security.SslStream::AuthenticateAsServer
- Custom ping payloads on Linux — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/7.0/ping-custom-payload-linux — excluded: not reachable from .NET Framework code
- Socket.End methods don't throw ObjectDisposedException — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/7.0/socket-end-closed-sockets — row: System.Net.Sockets.Socket::End

### .NET 7 — Serialization

- DataContractSerializer retains sign when deserializing -0 — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/7.0/datacontractserializer-negative-sign — excluded: covered by row System.Runtime.Serialization.DataContractSerializer::ReadObject(
- Deserialize Version type with leading or trailing whitespace — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/7.0/deserialize-version-with-whitespace — excluded: not a BCL member
- JsonSerializerOptions copy constructor includes JsonSerializerContext — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/7.0/jsonserializeroptions-copy-constructor — excluded: not a BCL member
- Polymorphic serialization for object types — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/7.0/polymorphic-serialization — excluded: not a BCL member
- System.Text.Json source generator fallback — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/7.0/reflection-fallback — excluded: not a BCL member

### .NET 7 — Windows Forms

- Obsoletions and warnings — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/7.0/obsolete-apis — excluded: build-time only
- Some APIs throw ArgumentNullException — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/7.0/apis-throw-argumentnullexception — row: System.Windows.Forms.ComboBox.ChildAccessibleObject::.ctor( ; System.Windows.Forms.ControlPaint::CreateHBitmap ; System.Windows.Forms.DataGridViewEditingControlShowingEventArgs::.ctor( ; System.Windows.Forms.ListView.CheckedIndexCollection::.ctor( ; System.Windows.Forms.ToolStripArrowRenderEventArgs::.ctor( ; System.Windows.Forms.ToolStripContentPanelRenderEventArgs::.ctor( ; System.Windows.Forms.ToolStripItemRenderEventArgs::.ctor( ; System.Windows.Forms.ToolStripPanelRenderEventArgs::.ctor(

### .NET 7 — WPF

- Restored drag-and-drop operations behavior on text editors — https://learn.microsoft.com/en-us/dotnet/core/compatibility/wpf/7.0/drag-and-drop — excluded: not reachable from .NET Framework code

### .NET 7 — XML and XSLT

- XmlSecureResolver is obsolete — https://learn.microsoft.com/en-us/dotnet/core/compatibility/xml/7.0/xmlsecureresolver-obsolete — row: System.Xml.XmlSecureResolver::

### .NET 8 — Core .NET libraries

- Activity operation name when null — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/activity-operation-name — excluded: not a BCL member
- AnonymousPipeServerStream.Dispose behavior — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/anonymouspipeserverstream-dispose — row: System.IO.Pipes.AnonymousPipeServerStream::Dispose(
- API obsoletions with custom diagnostic IDs — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/obsolete-apis-with-custom-diagnostics — excluded: build-time only
- Backslash mapping in Unix file paths — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/file-path-backslash — excluded: not reachable from .NET Framework code
- Base64.DecodeFromUtf8 methods ignore whitespace — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/decodefromutf8-whitespace — excluded: not a BCL member
- Boolean-backed enum type support removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/bool-backed-enum — excluded: not reachable from .NET Framework code
- Complex.ToString format changed to <a; b> — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/complex-format — row: System.Numerics.Complex::ToString(
- Drive's current directory path enumeration — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/drive-current-dir-paths — row: System.IO.Directory::Enumerate ; System.IO.Directory::GetFiles( ; System.IO.Directory::GetDirectories( ; System.IO.Directory::GetFileSystemEntries( ; System.IO.DirectoryInfo::Enumerate ; System.IO.DirectoryInfo::GetFiles( ; System.IO.DirectoryInfo::GetDirectories( ; System.IO.DirectoryInfo::GetFileSystemInfos(
- Enumerable.Sum throws new OverflowException for some inputs — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/enumerable-sum — row: System.Linq.Enumerable::Sum(
- FileStream writes when pipe is closed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/filestream-disposed-pipe — row: System.IO.FileStream::Write
- FindSystemTimeZoneById doesn't return new object — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/timezoneinfo-object — row: System.TimeZoneInfo::FindSystemTimeZoneById(
- GC.GetGeneration might return Int32.MaxValue — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/getgeneration-return-value — row: System.GC::GetGeneration(
- GetFolderPath behavior on Unix — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/getfolderpath-unix — excluded: not reachable from .NET Framework code
- GetSystemVersion no longer returns ImageRuntimeVersion — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/getsystemversion — row: System.Runtime.InteropServices.RuntimeEnvironment::GetSystemVersion(
- ITypeDescriptorContext nullable annotations — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/itypedescriptorcontext-props — excluded: build-time only
- LDAP APIs not available on .NET Standard / .NET Framework — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/ldap-netstandard-apis — excluded: not reachable from .NET Framework code
- Legacy Console.ReadKey removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/console-readkey-legacy — excluded: not reachable from .NET Framework code
- Method builders generate parameters with HasDefaultValue set to false — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/parameterinfo-hasdefaultvalue — row: System.Reflection.ParameterInfo::get_HasDefaultValue(
- Package part URIs are now compared case-insensitively in System.IO.Packaging — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/system-io-packaging-case-insensitive-uri — row: System.IO.Packaging.Package::
- ProcessStartInfo.WindowStyle honored when UseShellExecute is false — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/processstartinfo-windowstyle — excluded: covered by row System.Diagnostics.Process::Start(
- RuntimeIdentifier returns platform for which runtime was built — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/runtimeidentifier — excluded: not reachable from .NET Framework code
- Type.GetType throws exception for all invalid element types — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/type-gettype — row: System.Type::GetType(

### .NET 8 — Cryptography

- AesGcm authentication tag size on macOS — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/8.0/aesgcm-auth-tag-size — excluded: not reachable from .NET Framework code
- RSA.EncryptValue and RSA.DecryptValue obsolete — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/8.0/rsa-encrypt-decrypt-value-obsolete — excluded: covered by row System.Security.Cryptography.RSA::EncryptValue(

### .NET 8 — Extensions

- ActivatorUtilities.CreateInstance behaves consistently — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/8.0/activatorutilities-createinstance-behavior — excluded: not a BCL member
- ActivatorUtilities.CreateInstance requires non-null provider — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/8.0/activatorutilities-createinstance-null-provider — excluded: not a BCL member
- ConfigurationBinder silently skips invalid array elements — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/8.0/configurationbinder-skips-failed-array-elements — excluded: not a BCL member
- ConfigurationBinder throws for mismatched value — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/8.0/configurationbinder-exceptions — excluded: not a BCL member
- ConfigurationManager package no longer references System.Security.Permissions — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/8.0/configurationmanager-package — excluded: not a BCL member
- DirectoryServices package no longer references System.Security.Permissions — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/8.0/directoryservices-package — excluded: not a BCL member
- Empty keys added to dictionary by configuration binder — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/8.0/dictionary-configuration-binding — excluded: not a BCL member
- FromKeyedServicesAttribute.Key can be null — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/8.0/fromkeyedservicesattribute-key-nullable — excluded: not a BCL member
- HostApplicationBuilderSettings.Args respected by HostApplicationBuilder ctor — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/8.0/hostapplicationbuilder-ctor — excluded: not a BCL member
- ManagementDateTimeConverter.ToDateTime returns a local time — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/8.0/dmtf-todatetime — row: System.Management.ManagementDateTimeConverter::ToDateTime(
- System.Formats.Cbor DateTimeOffset formatting change — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/8.0/cbor-datetime — excluded: not a BCL member

### .NET 8 — Globalization

- Date and time converters honor culture argument — https://learn.microsoft.com/en-us/dotnet/core/compatibility/globalization/8.0/typeconverter-cultureinfo — row: System.ComponentModel.DateTimeConverter::ConvertTo( ; System.ComponentModel.DateTimeOffsetConverter::ConvertTo(
- TwoDigitYearMax default is 2049 — https://learn.microsoft.com/en-us/dotnet/core/compatibility/globalization/8.0/twodigityearmax-default — row: System.Globalization.Calendar::get_TwoDigitYearMax( ; System.Globalization.Calendar::ToFourDigitYear( ; System.DateTime::Parse ; System.DateTime::TryParse ; System.DateTimeOffset::Parse ; System.DateTimeOffset::TryParse

### .NET 8 — Interop

- CreateObjectFlags.Unwrap only unwraps on target instance — https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/8.0/comwrappers-unwrap — excluded: not reachable from .NET Framework code
- Custom marshallers require additional members — https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/8.0/marshal-modes — excluded: build-time only
- IDispatchImplAttribute API is removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/8.0/idispatchimplattribute-removed — excluded: build-time only
- JSFunctionBinding implicit public default constructor removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/8.0/jsfunctionbinding-constructor — excluded: not reachable from .NET Framework code
- SafeHandle types must have public constructor — https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/8.0/safehandle-constructor — excluded: build-time only
- Linux native library resolution no longer uses netcoredeps — https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/8.0/linux-netcoredeps — excluded: not reachable from .NET Framework code

### .NET 8 — Networking

- SendFile throws NotSupportedException for connectionless sockets — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/8.0/sendfile-connectionless — row: System.Net.Sockets.Socket::SendFile
- User info in mailto: URIs is compared — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/8.0/uri-comparison — row: System.Uri::Equals( ; System.Uri::op_Equality( ; System.Uri::op_Inequality(

### .NET 8 — Reflection

- IntPtr no longer used for function pointer types — https://learn.microsoft.com/en-us/dotnet/core/compatibility/reflection/8.0/function-pointer-reflection — row: System.Reflection.FieldInfo::get_FieldType( ; System.Reflection.PropertyInfo::get_PropertyType( ; System.Reflection.ParameterInfo::get_ParameterType(

### .NET 8 — Serialization

- BinaryFormatter disabled for most projects — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/8.0/binaryformatter-disabled — row: System.Runtime.Serialization.Formatters.Binary.BinaryFormatter::
- PublishedTrimmed projects fail reflection-based serialization — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/8.0/publishtrimmed — excluded: configuration only
- Reflection-based deserializer resolves metadata eagerly — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/8.0/metadata-resolving — excluded: not a BCL member

### .NET 8 — Windows Forms

- Certs checked before loading remote images in PictureBox — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/8.0/picturebox-remote-image — row: System.Windows.Forms.PictureBox::Load ; System.Windows.Forms.PictureBox::set_ImageLocation(
- DateTimePicker.Text is empty string — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/8.0/datetimepicker-text — row: System.Windows.Forms.DateTimePicker::get_Text(
- DefaultValueAttribute removed from some properties — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/8.0/defaultvalueattribute-removal — excluded: not a BCL member
- ExceptionCollection ctor throws ArgumentException — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/8.0/exceptioncollection — row: System.ComponentModel.Design.ExceptionCollection::.ctor(
- Forms scale according to AutoScaleMode — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/8.0/top-level-window-scaling — excluded: not a BCL member
- ImageList.ColorDepth default is Depth32Bit — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/8.0/imagelist-colordepth — row: System.Windows.Forms.ImageList::get_ColorDepth(
- System.Windows.Extensions doesn't reference System.Drawing.Common — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/8.0/extensions-package-deps — excluded: build-time only
- TableLayoutStyleCollection throws ArgumentException — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/8.0/tablelayoutstylecollection — row: System.Windows.Forms.TableLayoutStyleCollection::
- Top-level forms scale minimum and maximum size to DPI — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/8.0/forms-scale-size-to-dpi — excluded: configuration only
- WFDEV002 obsoletion is now an error — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/8.0/domainupdownaccessibleobject — excluded: build-time only

### .NET 9 — Core .NET libraries

- Adding a ZipArchiveEntry with CompressionLevel sets ZIP central directory header general-purpose bit flags — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/compressionlevel-bits — row: System.IO.Compression.ZipArchive::CreateEntry(string,System.IO.Compression.CompressionLevel)
- Altered UnsafeAccessor support for non-open generics — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/unsafeaccessor-generics — excluded: not reachable from .NET Framework code
- API obsoletions with custom diagnostic IDs — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/obsolete-apis-with-custom-diagnostics — excluded: build-time only
- Ambiguous overload resolution affecting StringValues implicit operators — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/ambiguous-overload — excluded: build-time only
- BigInteger maximum length — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/biginteger-limit — row: System.Numerics.BigInteger::
- BinaryReader.ReadString() returns "\uFFFD" on malformed sequences — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/binaryreader — row: System.IO.BinaryReader::ReadString(
- C# overload resolution prefers params span-type overloads — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/params-overloads — excluded: build-time only
- Creating type of array of System.Void not allowed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/type-instance — row: System.Type::MakeArrayType(
- Default Equals() and GetHashCode() throw for types marked with InlineArrayAttribute — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/inlinearrayattribute — excluded: not reachable from .NET Framework code
- EnumConverter validates registered types to be enum — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/enumconverter — row: System.ComponentModel.EnumConverter::.ctor(
- FromKeyedServicesAttribute no longer injects non-keyed parameter — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/non-keyed-params — excluded: not a BCL member
- IncrementingPollingCounter initial callback is asynchronous — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/async-callback — excluded: not reachable from .NET Framework code
- Inline array struct size limit is enforced — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/inlinearray-size — excluded: not reachable from .NET Framework code
- InMemoryDirectoryInfo prepends rootDir to files — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/inmemorydirinfo-prepends-rootdir — excluded: not a BCL member
- New TimeSpan.From*() overloads that take integers — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/timespan-from-overloads — excluded: build-time only
- New version of some OOB packages — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/oob-packages — excluded: build-time only
- RuntimeHelpers.GetSubArray returns different type — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/getsubarray-return — excluded: not reachable from .NET Framework code
- String.Trim(params ReadOnlySpan<char>) overload removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/string-trim — excluded: build-time only
- Support for empty environment variables — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/empty-env-variable — row: System.Environment::SetEnvironmentVariable( ; System.Diagnostics.ProcessStartInfo::get_Environment ; System.Diagnostics.ProcessStartInfo::get_EnvironmentVariables(
- ZipArchiveEntry names and comments respect UTF8 flag — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/9.0/ziparchiveentry-encoding — excluded: not reachable from .NET Framework code

### .NET 9 — Cryptography

- APIs Removed from System.Security.Cryptography.Pkcs netstandard2.0 — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/9.0/api-removed-pkcs — excluded: build-time only
- SafeEvpPKeyHandle.DuplicateHandle up-refs the handle — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/9.0/evp-pkey-handle — excluded: not reachable from .NET Framework code
- Some X509Certificate2 and X509Certificate constructors are obsolete — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/9.0/x509-certificates — excluded: build-time only
- Windows private key lifetime simplified — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/9.0/private-key-lifetime — row: System.Security.Cryptography.X509Certificates.X509Certificate2Collection::Import(

### .NET 9 — Interop

- CET supported by default — https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/9.0/cet-support — excluded: not a BCL member

### .NET 9 — JIT compiler

- Floating point to integer conversions are saturating — https://learn.microsoft.com/en-us/dotnet/core/compatibility/jit/9.0/fp-to-integer — excluded: not a BCL member
- Some SVE APIs removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/jit/9.0/sve-apis — excluded: build-time only

### .NET 9 — Networking

- HttpClient metrics report server.port unconditionally — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/9.0/server-port-attribute — excluded: not reachable from .NET Framework code
- HttpClientFactory logging redacts header values by default — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/9.0/redact-headers — excluded: not a BCL member
- HttpClientFactory uses SocketsHttpHandler as primary handler — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/9.0/default-handler — excluded: not a BCL member
- HttpListenerRequest.UserAgent is nullable — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/9.0/useragent-nullable — excluded: build-time only
- URI query redaction in HttpClient EventSource events — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/9.0/query-redaction-events — excluded: not reachable from .NET Framework code
- URI query redaction in IHttpClientFactory logs — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/9.0/query-redaction-logs — excluded: not a BCL member

### .NET 9 — Serialization

- BinaryFormatter always throws — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/9.0/binaryformatter-removal — excluded: covered by row System.Runtime.Serialization.Formatters.Binary.BinaryFormatter::
- Nullable JsonDocument properties deserialize to JsonValueKind.Null — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/9.0/jsondocument-props — excluded: not a BCL member
- System.Text.Json metadata reader now unescapes metadata property names — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/9.0/json-metadata-reader — excluded: not a BCL member

### .NET 9 — Windows Forms

- BindingSource.SortDescriptions doesn't return null — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/9.0/sortdescriptions-return-value — row: System.Windows.Forms.BindingSource::get_SortDescriptions(
- Changes to nullability annotations — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/9.0/nullability-changes — excluded: build-time only
- ComponentDesigner.Initialize throws ArgumentNullException — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/9.0/componentdesigner-initialize — row: System.ComponentModel.Design.ComponentDesigner::Initialize
- DataGridViewRowAccessibleObject.Name starting row index — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/9.0/datagridviewrowaccessibleobject-name-row — row: System.Windows.Forms.DataGridViewRow.DataGridViewRowAccessibleObject::get_Name(
- IMsoComponent support is opt-in — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/9.0/imsocomponent-support — excluded: not a BCL member
- New security analyzers — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/9.0/security-analyzers — excluded: build-time only
- No exception if DataGridView is null — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/9.0/datagridviewheadercell-nre — row: System.Windows.Forms.DataGridViewHeaderCell::Mouse
- PictureBox raises HttpClient exceptions — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/9.0/httpclient-exceptions — excluded: covered by row System.Windows.Forms.PictureBox::Load
- StatusStrip uses a different default renderer — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/9.0/statusstrip-renderer — excluded: not reachable from .NET Framework code

### .NET 9 — WPF

- GetXmlNamespaceMaps type change — https://learn.microsoft.com/en-us/dotnet/core/compatibility/wpf/9.0/xml-namespace-maps — row: System.Windows.Markup.XmlAttributeProperties::GetXmlNamespaceMaps(

### .NET 10 — Core .NET libraries

- API obsoletions — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/obsolete-apis — excluded: build-time only
- ActivitySource.CreateActivity and ActivitySource.StartActivity behavior change — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/activity-sampling — excluded: not a BCL member
- Arm64 SVE nonfaulting loads require mask — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/sve-nonfaulting-loads-mask-parameter — excluded: not reachable from .NET Framework code
- BufferedStream.WriteByte no longer performs implicit flush — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/bufferedstream-writebyte-flush — row: System.IO.BufferedStream::WriteByte(
- C# 14 overload resolution with span parameters — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/csharp-overload-resolution — excluded: build-time only
- Consistent shift behavior in generic math — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/generic-math — excluded: not reachable from .NET Framework code
- Default trace context propagator updated to W3C standard — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/default-trace-context-propagator — excluded: not a BCL member
- DriveInfo.DriveFormat returns Linux filesystem types — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/driveinfo-driveformat-linux — excluded: not reachable from .NET Framework code
- DynamicallyAccessedMembers annotation removed from DefaultValueAttribute ctor — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/defaultvalueattribute-dynamically-accessed-members — excluded: build-time only
- Explicit struct Size disallowed with InlineArray — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/inlinearray-explicit-size-disallowed — excluded: not reachable from .NET Framework code
- FilePatternMatch.Stem changed to non-nullable — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/filepatternmatch-stem-nonnullable — excluded: build-time only
- GnuTarEntry and PaxTarEntry no longer includes atime and ctime by default — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/tar-atime-ctime-default — excluded: not reachable from .NET Framework code
- LDAP DirectoryControl parsing is now more stringent — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/ldap-directorycontrol-parsing — row: System.DirectoryServices.Protocols.LdapConnection::SendRequest( ; System.DirectoryServices.Protocols.LdapConnection::EndSendRequest( ; System.DirectoryServices.Protocols.VlvRequestControl::.ctor(
- MacCatalyst version normalization — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/maccatalyst-version-normalization — excluded: not reachable from .NET Framework code
- .NET runtime no longer provides default termination signal handlers — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/sigterm-signal-handler — excluded: not reachable from .NET Framework code
- System.Linq.AsyncEnumerable included in core libraries — https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/asyncenumerable — excluded: build-time only
- Type.MakeGenericSignatureType argument validation — https://learn.microsoft.com/en-us/dotnet/core/compatibility/reflection/10/makegeneric-signaturetype-validation — excluded: not reachable from .NET Framework code

### .NET 10 — Cryptography

- CompositeMLDsa updated to draft-08 — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/10.0/composite-mldsa-draft-08 — excluded: not reachable from .NET Framework code
- CoseSigner.Key can be null — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/10.0/cosesigner-key-null — excluded: not reachable from .NET Framework code
- MLDsa and SlhDsa 'SecretKey' members renamed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/10.0/mldsa-slhdsa-secretkey-to-privatekey — excluded: build-time only
- OpenSSL cryptographic primitives aren't supported on macOS — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/10.0/openssl-macos-unsupported — excluded: not reachable from .NET Framework code
- OpenSSL 1.1.1 or later required on Unix — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/10.0/openssl-version-requirement — excluded: not reachable from .NET Framework code
- X500DistinguishedName validation is stricter — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/10.0/x500distinguishedname-validation — excluded: covered by row System.Security.Cryptography.X509Certificates.X500DistinguishedName::.ctor(
- X509Certificate and PublicKey key parameters can be null — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/10.0/x509-publickey-null — row: System.Security.Cryptography.X509Certificates.X509Certificate::GetKeyAlgorithmParameters ; System.Security.Cryptography.X509Certificates.PublicKey::get_EncodedParameters(
- Environment variable renamed to DOTNET_OPENSSL_VERSION_OVERRIDE — https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/10.0/version-override — excluded: configuration only

### .NET 10 — Extensions

- BackgroundService runs all of ExecuteAsync as a Task — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/backgroundservice-executeasync-task — excluded: not a BCL member
- Fix issues in GetKeyedService() and GetKeyedServices() with AnyKey — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/getkeyedservice-anykey — excluded: not a BCL member
- Null values preserved in configuration — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/configuration-null-values-preserved — excluded: not a BCL member
- Message no longer duplicated in Console log output — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/console-json-logging-duplicate-messages — excluded: not a BCL member
- ProviderAliasAttribute moved to Microsoft.Extensions.Logging.Abstractions assembly — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/provideraliasattribute-moved-assembly — excluded: build-time only
- Removed DynamicallyAccessedMembers annotation from trim-unsafe Microsoft.Extensions.Configuration code — https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/dynamically-accessed-members-configuration — excluded: build-time only

### .NET 10 — Globalization

- Environment variable renamed to DOTNET_ICU_VERSION_OVERRIDE — https://learn.microsoft.com/en-us/dotnet/core/compatibility/globalization/10.0/version-override — excluded: configuration only

### .NET 10 — Interop

- Casting IDispatchEx COM object to IReflect fails — https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/10.0/idispatchex-ireflect-cast — excluded: not a BCL member
- Single-file apps no longer look for native libraries in executable directory — https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/10.0/native-library-search — excluded: configuration only
- Specifying DllImportSearchPath.AssemblyDirectory only searches the assembly directory — https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/10.0/search-assembly-directory — excluded: not a BCL member

### .NET 10 — Networking

- HTTP/3 support disabled by default with PublishTrimmed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/10.0/http3-disabled-with-publishtrimmed — excluded: build-time only
- MailAddress enforces validation for consecutive dots — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/10.0/mailaddress-consecutive-dots — row: System.Net.Mail.MailAddress::.ctor(
- Streaming HTTP responses enabled by default in browser HTTP clients — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/10.0/default-http-streaming — excluded: not reachable from .NET Framework code
- Uri length limits removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/10.0/uri-length-limits-removed — row: System.Uri::.ctor( ; System.Uri::TryCreate(

### .NET 10 — Reflection

- More restricted annotations on InvokeMember/FindMembers/DeclaredMembers — https://learn.microsoft.com/en-us/dotnet/core/compatibility/reflection/10/ireflect-damt-annotations — excluded: build-time only

### .NET 10 — Serialization

- System.Text.Json checks for property name conflicts — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/10/property-name-validation — excluded: not a BCL member
- XmlSerializer no longer ignores properties marked with ObsoleteAttribute — https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/10/xmlserializer-obsolete-properties — row: System.Xml.Serialization.XmlSerializer::Serialize( ; System.Xml.Serialization.XmlSerializer::Deserialize(

### .NET 10 — Windows Forms

- API obsoletions — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/10.0/obsolete-apis — excluded: build-time only
- Applications referencing both WPF and WinForms must disambiguate MenuItem and ContextMenu types — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/10.0/menuitem-contextmenu — excluded: build-time only
- Renamed parameter in HtmlElement.InsertAdjacentElement — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/10.0/insertadjacentelement-orientation — excluded: build-time only
- TreeView checkbox image truncation — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/10.0/treeview-text-location — excluded: not a BCL member
- StatusStrip uses System RenderMode by default — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/10.0/statusstrip-renderer — excluded: not reachable from .NET Framework code
- System.Drawing OutOfMemoryException changed to ExternalException — https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/10.0/system-drawing-outofmemory-externalexception — row: System.Drawing.Image:: ; System.Drawing.Bitmap:: ; System.Drawing.Icon:: ; System.Drawing.Graphics::

### .NET 10 — Windows Presentation Foundation (WPF)

- Empty ColumnDefinitions and RowDefinitions are disallowed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/wpf/10.0/empty-grid-definitions — excluded: build-time only
- Incorrect usage of DynamicResource causes application crash — https://learn.microsoft.com/en-us/dotnet/core/compatibility/wpf/10.0/dynamicresource-crash — excluded: not a BCL member

### .NET Framework to .NET (porting) — Core .NET libraries

- Change in default value of UseShellExecute — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#change-in-default-value-of-useshellexecute — row: System.Diagnostics.Process::Start( ; System.Diagnostics.ProcessStartInfo::get_UseShellExecute(
- IDispatchImplAttribute API is removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#net-8 — excluded: build-time only
- UnauthorizedAccessException thrown by FileSystemInfo.Attributes — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#unauthorizedaccessexception-thrown-by-filesysteminfoattributes — row: System.IO.FileSystemInfo::set_Attributes(
- Handling corrupted-process-state exceptions is not supported — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#handling-corrupted-state-exceptions-is-not-supported — excluded: not a BCL member
- UriBuilder properties no longer prepend leading characters — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#uribuilder-properties-no-longer-prepend-leading-characters — row: System.UriBuilder::set_Fragment( ; System.UriBuilder::set_Query(
- Process.StartInfo throws InvalidOperationException for processes you didn't start — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#processstartinfo-throws-invalidoperationexception-for-processes-you-didnt-start — row: System.Diagnostics.Process::get_StartInfo(
- IDispatchImplAttribute API is removed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/8.0/idispatchimplattribute-removed — excluded: build-time only

### .NET Framework to .NET (porting) — Cryptography

- Boolean parameter of SignedCms.ComputeSignature is respected — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#boolean-parameter-of-signedcmscomputesignature-is-respected — row: System.Security.Cryptography.Pkcs.SignedCms::ComputeSignature(

### .NET Framework to .NET (porting) — Networking

- WebClient.CancelAsync doesn't always cancel immediately — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#webclientcancelasync-doesnt-always-cancel-immediately — row: System.Net.WebClient::CancelAsync(

### .NET Framework to .NET (porting) — Windows Forms

- Removed controls — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#removed-controls — excluded: build-time only
- CellFormatting event not raised if tooltip is shown — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#cellformatting-event-not-raised-if-tooltip-is-shown — excluded: not a BCL member
- Control.DefaultFont changed to Segoe UI 9 pt — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#default-control-font-changed-to-segoe-ui-9-pt — excluded: covered by row System.Windows.Forms.Control::get_DefaultFont(
- Modernization of the FolderBrowserDialog — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#modernization-of-the-folderbrowserdialog — excluded: not a BCL member
- SerializableAttribute removed from some Windows Forms types — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#serializableattribute-removed-from-some-windows-forms-types — excluded: covered by row System.Runtime.Serialization.Formatters.Binary.BinaryFormatter::
- AllowUpdateChildControlIndexForTabControls compatibility switch not supported — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#allowupdatechildcontrolindexfortabcontrols-compatibility-switch-not-supported — excluded: configuration only
- DomainUpDown.UseLegacyScrolling compatibility switch not supported — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#domainupdownuselegacyscrolling-compatibility-switch-not-supported — excluded: configuration only
- DoNotLoadLatestRichEditControl compatibility switch not supported — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#donotloadlatestricheditcontrol-compatibility-switch-not-supported — excluded: configuration only
- DoNotSupportSelectAllShortcutInMultilineTextBox compatibility switch not supported — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#donotsupportselectallshortcutinmultilinetextbox-compatibility-switch-not-supported — excluded: configuration only
- DontSupportReentrantFilterMessage compatibility switch not supported — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#dontsupportreentrantfiltermessage-compatibility-switch-not-supported — excluded: configuration only
- EnableVisualStyleValidation compatibility switch not supported — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#enablevisualstylevalidation-compatibility-switch-not-supported — excluded: configuration only
- UseLegacyContextMenuStripSourceControlValue compatibility switch not supported — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#uselegacycontextmenustripsourcecontrolvalue-compatibility-switch-not-supported — excluded: configuration only
- UseLegacyImages compatibility switch not supported — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#uselegacyimages-compatibility-switch-not-supported — excluded: configuration only
- About and SplashScreen templates are broken for Visual Basic — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#about-and-splashscreen-templates-are-broken — excluded: not a BCL member
- Types in Microsoft.VisualBasic.ApplicationServices namespace not available — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#types-in-microsoftvisualbasicapplicationservices-namespace-not-available — excluded: build-time only
- Types in Microsoft.VisualBasic.Devices namespace not available — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#types-in-microsoftvisualbasicdevices-namespace-not-available — excluded: build-time only
- Types in Microsoft.VisualBasic.MyServices namespace not available — https://learn.microsoft.com/en-us/dotnet/core/compatibility/fx-core#types-in-microsoftvisualbasicmyservices-namespace-not-available — excluded: build-time only

### .NET Core 3.1 — ASP.NET Core

- HTTP: Browser SameSite changes impact authentication — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.1#http-browser-samesite-changes-impact-authentication — excluded: not a BCL member

### .NET Core 3.1 — Windows Forms

- Removed controls — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.1#removed-controls — excluded: build-time only
- CellFormatting event not raised if tooltip is shown — https://learn.microsoft.com/en-us/dotnet/core/compatibility/3.1#cellformatting-event-not-raised-if-tooltip-is-shown — excluded: not a BCL member

### APIs that always throw on .NET — System

- System.AppDomain.CreateDomain* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#system — row: System.AppDomain::CreateDomain(
- System.AppDomain.ExecuteAssembly(System.String,System.String[],System.Byte[],System.Configuration.Assemblies.AssemblyHashAlgorithm) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#system — row: System.AppDomain::ExecuteAssembly(string,string[],byte[],
- System.AppDomain.Unload(System.AppDomain) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#system — row: System.AppDomain::Unload(
- System.Console.CapsLock — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#system — excluded: not reachable from .NET Framework code
- System.Console.NumberLock — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#system — excluded: not reachable from .NET Framework code
- System.Delegate.GetObjectData(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#system — row: System.Delegate::GetObjectData(
- System.Exception.SerializeObjectState — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#system — row: System.Exception::add_SerializeObjectState(
- System.MarshalByRefObject.GetLifetimeService — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#system — row: System.MarshalByRefObject::GetLifetimeService(
- System.MarshalByRefObject.InitializeLifetimeService — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#system — row: System.MarshalByRefObject::InitializeLifetimeService(
- System.OperatingSystem.GetObjectData(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#system — row: System.OperatingSystem::GetObjectData(
- System.Type.ReflectionOnlyGetType(System.String,System.Boolean,System.Boolean) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#system — row: System.Type::ReflectionOnlyGetType(

### APIs that always throw on .NET — System.CodeDom.Compiler

- System.CodeDom.Compiler.CodeDomProvider.CompileAssemblyFromDom* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemcodedomcompiler — row: System.CodeDom.Compiler.CodeDomProvider::CompileAssemblyFromDom(
- System.CodeDom.Compiler.CodeDomProvider.CompileAssemblyFromFile* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemcodedomcompiler — row: System.CodeDom.Compiler.CodeDomProvider::CompileAssemblyFromFile(
- System.CodeDom.Compiler.CodeDomProvider.CompileAssemblyFromSource* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemcodedomcompiler — row: System.CodeDom.Compiler.CodeDomProvider::CompileAssemblyFromSource(

### APIs that always throw on .NET — System.Collections.Specialized

- System.Collections.Specialized.NameObjectCollectionBase.#ctor(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemcollectionsspecialized — row: System.Collections.Specialized.NameObjectCollectionBase::.ctor(System.Runtime.Serialization.SerializationInfo
- System.Collections.Specialized.NameObjectCollectionBase.GetObjectData(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemcollectionsspecialized — row: System.Collections.Specialized.NameObjectCollectionBase::GetObjectData(
- System.Collections.Specialized.NameObjectCollectionBase.OnDeserialization(System.Object) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemcollectionsspecialized — row: System.Collections.Specialized.NameObjectCollectionBase::OnDeserialization(

### APIs that always throw on .NET — System.Configuration

- System.Configuration.RsaProtectedConfigurationProvider (all members) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemconfiguration — row: System.Configuration.RsaProtectedConfigurationProvider::

### APIs that always throw on .NET — System.Console

- System.Console.Beep — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemconsole — excluded: not reachable from .NET Framework code
- System.Console.BufferHeight (set only) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemconsole — excluded: not reachable from .NET Framework code
- System.Console.BufferWidth (set only) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemconsole — excluded: not reachable from .NET Framework code
- System.Console.CursorSize (set only) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemconsole — excluded: not reachable from .NET Framework code
- System.Console.CursorVisible (get only) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemconsole — excluded: not reachable from .NET Framework code
- System.Console.MoveBufferArea* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemconsole — excluded: not reachable from .NET Framework code
- System.Console.SetWindowPosition* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemconsole — excluded: not reachable from .NET Framework code
- System.Console.SetWindowSize* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemconsole — excluded: not reachable from .NET Framework code
- System.Console.Title (get only) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemconsole — excluded: not reachable from .NET Framework code
- System.Console.WindowHeight (set only) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemconsole — excluded: not reachable from .NET Framework code
- System.Console.WindowLeft (set only) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemconsole — excluded: not reachable from .NET Framework code
- System.Console.WindowTop (set only) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemconsole — excluded: not reachable from .NET Framework code
- System.Console.WindowWidth (set only) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemconsole — excluded: not reachable from .NET Framework code

### APIs that always throw on .NET — System.Diagnostics.Process

- System.Diagnostics.Process.MaxWorkingSet (set only) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemdiagnosticsprocess — excluded: not reachable from .NET Framework code
- System.Diagnostics.Process.MinWorkingSet (set only) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemdiagnosticsprocess — excluded: not reachable from .NET Framework code
- System.Diagnostics.Process.ProcessorAffinity — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemdiagnosticsprocess — excluded: not reachable from .NET Framework code
- System.Diagnostics.Process.MainWindowHandle — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemdiagnosticsprocess — excluded: not reachable from .NET Framework code
- System.Diagnostics.Process.Start(System.String,System.String,System.String,System.Security.SecureString,System.String) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemdiagnosticsprocess — excluded: not reachable from .NET Framework code
- System.Diagnostics.Process.Start(System.String,System.String,System.Security.SecureString,System.String) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemdiagnosticsprocess — excluded: not reachable from .NET Framework code
- System.Diagnostics.ProcessStartInfo.UserName — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemdiagnosticsprocess — excluded: not reachable from .NET Framework code
- System.Diagnostics.ProcessStartInfo.PasswordInClearText — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemdiagnosticsprocess — excluded: not reachable from .NET Framework code
- System.Diagnostics.ProcessStartInfo.Domain — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemdiagnosticsprocess — excluded: not reachable from .NET Framework code
- System.Diagnostics.ProcessStartInfo.LoadUserProfile — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemdiagnosticsprocess — excluded: not reachable from .NET Framework code
- System.Diagnostics.ProcessThread.BasePriority (set only) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemdiagnosticsprocess — excluded: not reachable from .NET Framework code
- System.Diagnostics.ProcessThread.BasePriority (get only) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemdiagnosticsprocess — excluded: not reachable from .NET Framework code
- System.Diagnostics.ProcessThread.ProcessorAffinity (set only) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemdiagnosticsprocess — excluded: not reachable from .NET Framework code

### APIs that always throw on .NET — System.IO

- System.IO.FileSystemInfo.#ctor(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemio — row: System.IO.FileSystemInfo::.ctor(System.Runtime.Serialization.SerializationInfo
- System.IO.FileSystemInfo.GetObjectData(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemio — row: System.IO.FileSystemInfo::GetObjectData(

### APIs that always throw on .NET — System.IO.Pipes

- System.IO.Pipes.NamedPipeClientStream.NumberOfServerInstances — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemiopipes — excluded: not reachable from .NET Framework code
- System.IO.Pipes.NamedPipeServerStream.GetImpersonationUserName — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemiopipes — excluded: not reachable from .NET Framework code
- System.IO.Pipes.PipeStream.InBufferSize — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemiopipes — excluded: not reachable from .NET Framework code
- System.IO.Pipes.PipeStream.OutBufferSize — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemiopipes — excluded: not reachable from .NET Framework code
- System.IO.Pipes.PipeStream.ReadMode (set only) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemiopipes — excluded: not reachable from .NET Framework code
- System.IO.Pipes.PipeStream.WaitForPipeDrain — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemiopipes — excluded: not reachable from .NET Framework code

### APIs that always throw on .NET — System.Media

- System.Media.SoundPlayer.#ctor(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemmedia — row: System.Media.SoundPlayer::.ctor(System.Runtime.Serialization.SerializationInfo

### APIs that always throw on .NET — System.Net

- System.Net.AuthenticationManager — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnet — row: System.Net.AuthenticationManager::
- System.Net.AuthenticationManager.Authenticate(System.String,System.Net.WebRequest,System.Net.ICredentials) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnet — excluded: covered by row System.Net.AuthenticationManager::
- System.Net.AuthenticationManager.PreAuthenticate(System.Net.WebRequest,System.Net.ICredentials) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnet — excluded: covered by row System.Net.AuthenticationManager::
- System.Net.FileWebRequest.#ctor(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnet — row: System.Net.FileWebRequest::.ctor(System.Runtime.Serialization.SerializationInfo
- System.Net.FileWebRequest.GetObjectData(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnet — row: System.Net.FileWebRequest::GetObjectData(
- System.Net.FileWebResponse.#ctor(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnet — row: System.Net.FileWebResponse::.ctor(System.Runtime.Serialization.SerializationInfo
- System.Net.FileWebResponse.GetObjectData(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnet — row: System.Net.FileWebResponse::GetObjectData(
- System.Net.HttpWebRequest.#ctor(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnet — row: System.Net.HttpWebRequest::.ctor(System.Runtime.Serialization.SerializationInfo
- System.Net.HttpWebRequest.GetObjectData(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnet — row: System.Net.HttpWebRequest::GetObjectData(
- System.Net.HttpWebResponse.#ctor(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnet — row: System.Net.HttpWebResponse::.ctor(System.Runtime.Serialization.SerializationInfo
- System.Net.HttpWebResponse.GetObjectData(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnet — row: System.Net.HttpWebResponse::GetObjectData(
- System.Net.WebProxy.#ctor(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnet — row: System.Net.WebProxy::.ctor(System.Runtime.Serialization.SerializationInfo
- System.Net.WebProxy.GetDefaultProxy — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnet — row: System.Net.WebProxy::GetDefaultProxy(
- System.Net.WebProxy.GetObjectData* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnet — row: System.Net.WebProxy::GetObjectData(
- System.Net.WebRequest.#ctor(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnet — row: System.Net.WebRequest::.ctor(System.Runtime.Serialization.SerializationInfo
- System.Net.WebRequest.GetObjectData(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnet — row: System.Net.WebRequest::GetObjectData(
- System.Net.WebResponse.#ctor(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnet — row: System.Net.WebResponse::.ctor(System.Runtime.Serialization.SerializationInfo
- System.Net.WebResponse.GetObjectData(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnet — row: System.Net.WebResponse::GetObjectData(

### APIs that always throw on .NET — System.Net.NetworkInformation

- System.Net.NetworkInformation.Ping.Send* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnetnetworkinformation — excluded: not reachable from .NET Framework code

### APIs that always throw on .NET — System.Net.Sockets

- System.Net.Sockets.Socket.#ctor(System.Net.Sockets.SocketInformation) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnetsockets — row: System.Net.Sockets.Socket::.ctor(System.Net.Sockets.SocketInformation)
- System.Net.Sockets.Socket.DuplicateAndClose(System.Int32) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnetsockets — row: System.Net.Sockets.Socket::DuplicateAndClose(

### APIs that always throw on .NET — System.Net.WebSockets

- System.Net.WebSockets.WebSocket.RegisterPrefixes — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemnetwebsockets — row: System.Net.WebSockets.WebSocket::RegisterPrefixes(

### APIs that always throw on .NET — System.Reflection

- System.Reflection.Assembly.CodeBase — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemreflection — row: System.Reflection.Assembly::get_CodeBase(
- System.Reflection.Assembly.EscapedCodeBase — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemreflection — row: System.Reflection.Assembly::get_EscapedCodeBase(
- System.Reflection.Assembly.LoadFrom(System.String,System.Byte[],System.Configuration.Assemblies.AssemblyHashAlgorithm) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemreflection — row: System.Reflection.Assembly::LoadFrom(string,byte[],
- System.Reflection.Assembly.ReflectionOnlyLoad* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemreflection — row: System.Reflection.Assembly::ReflectionOnlyLoad(
- System.Reflection.Assembly.ReflectionOnlyLoadFrom(System.String) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemreflection — row: System.Reflection.Assembly::ReflectionOnlyLoadFrom(
- System.Reflection.AssemblyName.GetObjectData(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemreflection — row: System.Reflection.AssemblyName::GetObjectData(
- System.Reflection.AssemblyName.KeyPair — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemreflection — row: System.Reflection.AssemblyName::get_KeyPair( ; System.Reflection.AssemblyName::set_KeyPair(
- System.Reflection.AssemblyName.OnDeserialization(System.Object) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemreflection — row: System.Reflection.AssemblyName::OnDeserialization(
- System.Reflection.StrongNameKeyPair.#ctor* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemreflection — row: System.Reflection.StrongNameKeyPair::.ctor(
- System.Reflection.StrongNameKeyPair.PublicKey — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemreflection — row: System.Reflection.StrongNameKeyPair::get_PublicKey(

### APIs that always throw on .NET — System.Runtime.CompilerServices

- System.Runtime.CompilerServices.DebugInfoGenerator.CreatePdbGenerator — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemruntimecompilerservices — row: System.Runtime.CompilerServices.DebugInfoGenerator::CreatePdbGenerator(

### APIs that always throw on .NET — System.Runtime.InteropServices

- System.Runtime.InteropServices.IDispatchImplAttribute — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemruntimeinteropservices — excluded: build-time only
- System.Runtime.InteropServices.Marshal.GetIDispatchForObject(System.Object) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemruntimeinteropservices — row: System.Runtime.InteropServices.Marshal::GetIDispatchForObject(
- System.Runtime.InteropServices.RuntimeEnvironment.SystemConfigurationFile — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemruntimeinteropservices — row: System.Runtime.InteropServices.RuntimeEnvironment::get_SystemConfigurationFile(
- System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeInterfaceAsIntPtr(System.Guid,System.Guid) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemruntimeinteropservices — row: System.Runtime.InteropServices.RuntimeEnvironment::GetRuntimeInterfaceAsIntPtr(
- System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeInterfaceAsObject(System.Guid,System.Guid) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemruntimeinteropservices — row: System.Runtime.InteropServices.RuntimeEnvironment::GetRuntimeInterfaceAsObject(
- System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeMarshal.StringToHString(System.String) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemruntimeinteropservices — excluded: not reachable from .NET Framework code
- System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeMarshal.PtrToStringHString(System.IntPtr) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemruntimeinteropservices — excluded: not reachable from .NET Framework code
- System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeMarshal.FreeHString(System.IntPtr) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemruntimeinteropservices — excluded: not reachable from .NET Framework code

### APIs that always throw on .NET — System.Runtime.Serialization

- System.Runtime.Serialization.Formatters.Binary.BinaryFormatter.Serialize(System.IO.Stream,System.Object) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemruntimeserialization — excluded: covered by row System.Runtime.Serialization.Formatters.Binary.BinaryFormatter::
- System.Runtime.Serialization.Formatters.Binary.BinaryFormatter.Serialize(System.IO.Stream,System.Object) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemruntimeserialization — excluded: covered by row System.Runtime.Serialization.Formatters.Binary.BinaryFormatter::
- System.Runtime.Serialization.Formatters.Binary.BinaryFormatter.Deserialize(System.IO.Stream) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemruntimeserialization — excluded: covered by row System.Runtime.Serialization.Formatters.Binary.BinaryFormatter::
- System.Runtime.Serialization.XsdDataContractExporter.Schemas — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemruntimeserialization — row: System.Runtime.Serialization.XsdDataContractExporter::get_Schemas(

### APIs that always throw on .NET — System.Security

- System.Security.CodeAccessPermission.Deny — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecurity — row: System.Security.CodeAccessPermission::Deny(
- System.Security.CodeAccessPermission.PermitOnly — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecurity — row: System.Security.CodeAccessPermission::PermitOnly(
- System.Security.PermissionSet.ConvertPermissionSet(System.String,System.Byte[],System.String) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecurity — row: System.Security.PermissionSet::ConvertPermissionSet(
- System.Security.PermissionSet.Deny — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecurity — row: System.Security.PermissionSet::Deny(
- System.Security.PermissionSet.PermitOnly — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecurity — row: System.Security.PermissionSet::PermitOnly(
- System.Security.SecurityContext.Capture — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecurity — row: System.Security.SecurityContext::Capture(
- System.Security.SecurityContext.CreateCopy — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecurity — row: System.Security.SecurityContext::CreateCopy(
- System.Security.SecurityContext.Dispose — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecurity — row: System.Security.SecurityContext::Dispose(
- System.Security.SecurityContext.IsFlowSuppressed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecurity — row: System.Security.SecurityContext::IsFlowSuppressed(
- System.Security.SecurityContext.IsWindowsIdentityFlowSuppressed — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecurity — row: System.Security.SecurityContext::IsWindowsIdentityFlowSuppressed(
- System.Security.SecurityContext.RestoreFlow — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecurity — row: System.Security.SecurityContext::RestoreFlow(
- System.Security.SecurityContext.Run(System.Security.SecurityContext,System.Threading.ContextCallback,System.Object) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecurity — row: System.Security.SecurityContext::Run(
- System.Security.SecurityContext.SuppressFlow — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecurity — row: System.Security.SecurityContext::SuppressFlow(
- System.Security.SecurityContext.SuppressFlowWindowsIdentity — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecurity — row: System.Security.SecurityContext::SuppressFlowWindowsIdentity(

### APIs that always throw on .NET — System.Security.Claims

- System.Security.Claims.ClaimsPrincipal.#ctor(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecurityclaims — row: System.Security.Claims.ClaimsPrincipal::.ctor(System.Runtime.Serialization.SerializationInfo
- System.Security.Claims.ClaimsPrincipal.GetObjectData(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecurityclaims — row: System.Security.Claims.ClaimsPrincipal::GetObjectData(
- System.Security.Claims.ClaimsIdentity.#ctor(System.Runtime.Serialization.SerializationInfo) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecurityclaims — row: System.Security.Claims.ClaimsIdentity::.ctor(System.Runtime.Serialization.SerializationInfo
- System.Security.Claims.ClaimsIdentity.#ctor(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecurityclaims — row: System.Security.Claims.ClaimsIdentity::.ctor(System.Runtime.Serialization.SerializationInfo
- System.Security.Claims.ClaimsIdentity.GetObjectData(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecurityclaims — row: System.Security.Claims.ClaimsIdentity::GetObjectData(

### APIs that always throw on .NET — System.Security.Cryptography

- System.Security.Cryptography.AesCcm.#ctor* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.AsymmetricAlgorithm.Create(System.String) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.AsymmetricAlgorithm::Create(string)
- System.Security.Cryptography.CngAlgorithm — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.CngAlgorithmGroup — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.CngKey — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.CngKeyBlobFormat — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.CngKeyCreationParameters — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.CngProvider — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.CngUIPolicy — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.CryptoConfig.EncodeOID(System.String) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.CryptoConfig::EncodeOID(
- System.Security.Cryptography.CspKeyContainerInfo.#ctor* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.CspKeyContainerInfo.Accessible — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.CspKeyContainerInfo.Exportable — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.CspKeyContainerInfo.HardwareDevice — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.CspKeyContainerInfo.KeyContainerName — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.CspKeyContainerInfo.KeyNumber — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.CspKeyContainerInfo.MachineKeyStore — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.CspKeyContainerInfo.Protected — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.CspKeyContainerInfo.ProviderName — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.CspKeyContainerInfo.ProviderType — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.CspKeyContainerInfo.RandomlyGenerated — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.CspKeyContainerInfo.Removable — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.CspKeyContainerInfo.UniqueKeyContainerName — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.DSA.Create* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.DSACryptoServiceProvider.#ctor* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.X509Certificates.DSACertificateExtensions.GetDSAPrivateKey(System.Security.Cryptography.X509Certificates.X509Certificate2) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.X509Certificates.DSACertificateExtensions.GetDSAPublicKey(System.Security.Cryptography.X509Certificates.X509Certificate2) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.X509Certificates.DSACertificateExtensions.CopyWithPrivateKey(System.Security.Cryptography.X509Certificates.X509Certificate2,System.Security.Cryptography.DSA) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.DSAOpenSsl.#ctor* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.ECDiffieHellmanCng.FromXmlString(System.String,System.Security.Cryptography.ECKeyXmlFormat) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.ECDiffieHellmanCng::FromXmlString(string,System.Security.Cryptography.ECKeyXmlFormat)
- System.Security.Cryptography.ECDiffieHellmanCng.ToXmlString(System.Security.Cryptography.ECKeyXmlFormat) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.ECDiffieHellmanCng::ToXmlString(System.Security.Cryptography.ECKeyXmlFormat)
- System.Security.Cryptography.ECDiffieHellmanCngPublicKey.FromXmlString(System.String) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.ECDiffieHellmanCngPublicKey::FromXmlString(
- System.Security.Cryptography.ECDiffieHellmanCngPublicKey.ToXmlString — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.ECDiffieHellmanCngPublicKey::ToXmlString(
- System.Security.Cryptography.ECDiffieHellmanOpenSsl.#ctor* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.ECDiffieHellmanPublicKey.ToByteArray — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.ECDiffieHellmanPublicKey.ToXmlString — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.ECDiffieHellmanPublicKey::ToXmlString(
- System.Security.Cryptography.ECDsaCng.FromXmlString(System.String,System.Security.Cryptography.ECKeyXmlFormat) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.ECDsaCng::FromXmlString(string,System.Security.Cryptography.ECKeyXmlFormat)
- System.Security.Cryptography.ECDsaCng.ToXmlString(System.Security.Cryptography.ECKeyXmlFormat) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.ECDsaCng::ToXmlString(System.Security.Cryptography.ECKeyXmlFormat)
- System.Security.Cryptography.ECDsaOpenSsl.#ctor* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.HashAlgorithm.Create — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.HashAlgorithm::Create()
- System.Security.Cryptography.HMAC.Create — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.HMAC::Create()
- System.Security.Cryptography.HMAC.Create(System.String) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.HMAC::Create(string)
- System.Security.Cryptography.HMAC.HashCore* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.HMAC::HashCore(
- System.Security.Cryptography.HMAC.HashFinal* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.HMAC::HashFinal(
- System.Security.Cryptography.HMAC.Initialize* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.HMAC::Initialize(
- System.Security.Cryptography.KeyedHashAlgorithm.Create — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.KeyedHashAlgorithm::Create()
- System.Security.Cryptography.KeyedHashAlgorithm.Create(System.String) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.KeyedHashAlgorithm::Create(string)
- System.Security.Cryptography.ProtectedData.Protect* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.ProtectedData.Unprotect* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.RSACryptoServiceProvider.DecryptValue(System.Byte[]) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.RSACryptoServiceProvider::DecryptValue(
- System.Security.Cryptography.RSACryptoServiceProvider.EncryptValue(System.Byte[]) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.RSACryptoServiceProvider::EncryptValue(
- System.Security.Cryptography.RSAOpenSsl.#ctor* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.RSA.DecryptValue(System.Byte[]) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.RSA::DecryptValue(
- System.Security.Cryptography.RSA.EncryptValue(System.Byte[]) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.RSA::EncryptValue(
- System.Security.Cryptography.RSA.FromXmlString* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.RSA::FromXmlString(
- System.Security.Cryptography.RSA.ToXmlString* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.RSA::ToXmlString(
- System.Security.Cryptography.SafeEvpPKeyHandle — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — excluded: not reachable from .NET Framework code
- System.Security.Cryptography.SymmetricAlgorithm.Create — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.SymmetricAlgorithm::Create()
- System.Security.Cryptography.SymmetricAlgorithm.Create(System.String) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptography — row: System.Security.Cryptography.SymmetricAlgorithm::Create(string)

### APIs that always throw on .NET — System.Security.Cryptography.Pkcs

- System.Security.Cryptography.Pkcs.CmsSigner.#ctor(System.Security.Cryptography.CspParameters) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptographypkcs — row: System.Security.Cryptography.Pkcs.CmsSigner::.ctor(System.Security.Cryptography.CspParameters)
- System.Security.Cryptography.Pkcs.SignerInfo.ComputeCounterSignature — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptographypkcs — row: System.Security.Cryptography.Pkcs.SignerInfo::ComputeCounterSignature()

### APIs that always throw on .NET — System.Security.Cryptography.X509Certificates

- System.Security.Cryptography.X509Certificates.X509Certificate.#ctor(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptographyx509certificates — row: System.Security.Cryptography.X509Certificates.X509Certificate::.ctor(System.Runtime.Serialization.SerializationInfo
- System.Security.Cryptography.X509Certificates.X509Certificate.Import* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptographyx509certificates — row: System.Security.Cryptography.X509Certificates.X509Certificate::Import( ; System.Security.Cryptography.X509Certificates.X509Certificate2::Import(
- System.Security.Cryptography.X509Certificates.X509Certificate2.#ctor(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptographyx509certificates — row: System.Security.Cryptography.X509Certificates.X509Certificate2::.ctor(System.Runtime.Serialization.SerializationInfo
- System.Security.Cryptography.X509Certificates.X509Certificate2.PrivateKey (set only) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritycryptographyx509certificates — row: System.Security.Cryptography.X509Certificates.X509Certificate2::set_PrivateKey(

### APIs that always throw on .NET — System.Security.Authentication.ExtendedProtection

- System.Security.Authentication.ExtendedProtection.ExtendedProtectionPolicy.#ctor(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecurityauthenticationextendedprotection — row: System.Security.Authentication.ExtendedProtection.ExtendedProtectionPolicy::.ctor(System.Runtime.Serialization.SerializationInfo

### APIs that always throw on .NET — System.Security.Policy

- System.Security.Policy.Hash.GetObjectData(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemsecuritypolicy — row: System.Security.Policy.Hash::GetObjectData(

### APIs that always throw on .NET — System.ServiceProcess.ServiceController

- System.ServiceProcess.TimeoutException.#ctor(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemserviceprocessservicecontroller — row: System.ServiceProcess.TimeoutException::.ctor(System.Runtime.Serialization.SerializationInfo

### APIs that always throw on .NET — System.Text.RegularExpressions

- System.Text.RegularExpressions.Regex.CompileToAssembly* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemtextregularexpressions — row: System.Text.RegularExpressions.Regex::CompileToAssembly(

### APIs that always throw on .NET — System.Threading

- System.Threading.CompressedStack.GetObjectData(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemthreading — row: System.Threading.CompressedStack::GetObjectData(
- System.Threading.ExecutionContext.GetObjectData(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemthreading — row: System.Threading.ExecutionContext::GetObjectData(
- System.Threading.Thread.Abort* — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemthreading — row: System.Threading.Thread::Abort(
- System.Threading.Thread.ResetAbort — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemthreading — row: System.Threading.Thread::ResetAbort(
- System.Threading.Thread.Resume — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemthreading — row: System.Threading.Thread::Resume(
- System.Threading.Thread.Suspend — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemthreading — row: System.Threading.Thread::Suspend(

### APIs that always throw on .NET — System.Xml

- System.Xml.XmlDictionaryReader.CreateMtomReader(System.Byte[],System.Int32,System.Int32,System.Text.Encoding[],System.String,System.Xml.XmlDictionaryReaderQuotas,System.Int32,System.Xml.OnXmlDictionaryReaderClose) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemxml — row: System.Xml.XmlDictionaryReader::CreateMtomReader(
- System.Xml.XmlDictionaryReader.CreateMtomReader(System.IO.Stream,System.Text.Encoding[],System.String,System.Xml.XmlDictionaryReaderQuotas,System.Int32,System.Xml.OnXmlDictionaryReaderClose) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemxml — row: System.Xml.XmlDictionaryReader::CreateMtomReader(
- System.Xml.XmlDictionaryWriter.CreateMtomWriter(System.IO.Stream,System.Text.Encoding,System.Int32,System.String,System.String,System.String,System.Boolean,System.Boolean) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemxml — row: System.Xml.XmlDictionaryWriter::CreateMtomWriter(
- System.Xml.Xsl.XsltSettings.EnableScript (when set to `true`) — https://learn.microsoft.com/en-us/dotnet/core/compatibility/unsupported-apis#systemxml — row: System.Xml.Xsl.XsltSettings::set_EnableScript( ; System.Xml.Xsl.XsltSettings::.ctor(bool,bool)

## Curated rows with no compatibility-page entry

- System.String::GetHashCode( — https://learn.microsoft.com/en-us/dotnet/api/system.string.gethashcode?view=net-10.0 — row: System.String::GetHashCode(
- System.Text.Encoding::get_Default( — https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.default?view=net-10.0 — row: System.Text.Encoding::get_Default(

## Measured (ADR 0035, ticket M3-033)

Rows added from `tools/runtime-diff` runs against the corpus (`docs/runs/2026-09-26-runtime-diff/SUMMARY.md`),
not from a compatibility page: the behaviour change predates .NET Core 3.0, the earliest release this
review covers, so no page above names it. Each row's witness is in `runtime-changes.json` itself.

- Path.Combine no longer validates its arguments — https://learn.microsoft.com/en-us/dotnet/api/system.io.path.combine — row: System.IO.Path::Combine(string,string)
- Path.GetDirectoryName no longer validates its argument — https://learn.microsoft.com/en-us/dotnet/api/system.io.path.getdirectoryname — row: System.IO.Path::GetDirectoryName(string)
- StreamReader's constructor throws IOException, not ArgumentException, for a path with characters the file system rejects — https://learn.microsoft.com/en-us/dotnet/api/system.io.streamreader.-ctor — row: System.IO.StreamReader::.ctor(string)
