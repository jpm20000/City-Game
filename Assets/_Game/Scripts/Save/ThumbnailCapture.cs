using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Renders the map camera into a small PNG for a save's thumbnail (M19a). Camera renders never include the overlay UI,
// so there is no HUD in the picture. Uses URP's render request, and a plain Camera.Render as the fallback.
public static class ThumbnailCapture
{
    public const int Width = 320;
    public const int Height = 180;

    // PNG bytes, or null when there is no camera or the render failed.
    public static byte[] Capture(Camera camera)
    {
        if (camera == null) return null;

        RenderTexture full = RenderTexture.GetTemporary(Width * 2, Height * 2, 24, RenderTextureFormat.ARGB32);
        RenderTexture small = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32);
        RenderTexture previousActive = RenderTexture.active;
        Texture2D texture = null;
        try
        {
            Render(camera, full);
            Graphics.Blit(full, small);
            RenderTexture.active = small;
            texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply(false);
            return texture.EncodeToPNG();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"ThumbnailCapture: could not render a thumbnail ({e.Message}).");
            return null;
        }
        finally
        {
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(full);
            RenderTexture.ReleaseTemporary(small);
            if (texture != null) Object.Destroy(texture);
        }
    }

    private static void Render(Camera camera, RenderTexture target)
    {
        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
        if (RenderPipeline.SupportsRenderRequest(camera, request))
        {
            RenderPipeline.SubmitRenderRequest(camera, request);
            return;
        }

        RenderTexture previous = camera.targetTexture;
        camera.targetTexture = target;
        camera.Render();
        camera.targetTexture = previous;
    }
}
