using Microsoft.AspNetCore.Mvc;

namespace Equiv.Samples.WebApiInheritedRoute;

[ApiController]
[Route("[controller]")]
public abstract class BaseApiController : ControllerBase
{
}

public class BrandingController : BaseApiController
{
    [HttpGet("Configuration")]
    public int GetBrandingOptions() => 1;
}

public class StartupController : BaseApiController
{
    [HttpGet("Configuration")]
    public int GetStartupConfiguration() => 2;
}

public class AudioController : BaseApiController
{
    [HttpGet("{itemId}/stream")]
    [HttpHead("{itemId}/stream")]
    public int GetAudioStream(int itemId) => itemId + 1;
}

public class VideosController : BaseApiController
{
    [HttpGet("{itemId}/stream")]
    [HttpHead("{itemId}/stream")]
    public int GetVideoStream(int itemId) => itemId + 2;
}
