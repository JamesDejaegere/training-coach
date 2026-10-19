using Microsoft.AspNetCore.Components.Web;

namespace TrainingCoach.Web;

public static class RenderModes
{
    /// <summary>Interactive server without prerendering, so pages load their data once instead of twice.</summary>
    public static readonly InteractiveServerRenderMode InteractiveServerNoPrerender = new(prerender: false);
}
