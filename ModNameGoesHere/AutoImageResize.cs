using HarmonyLib;
using NeosModLoader;
using FrooxEngine;
using System.Threading.Tasks;
using BaseX;
using CodeX;
using System;
using System.Drawing;
using System.IO;

namespace ModNameGoesHere
{
    public class AutoImageResize : NeosMod
    {
        public static int MAX_SIZE = 2048;
        public override string Name => "AutoImageResize";
        public override string Author => "dfgHiatus";
        public override string Version => "1.0.0";
        public override string Link => "https://github.com/dfgHiatus/AutoImageResize/";
        public override void OnEngineInit()
        {
            Harmony harmony = new Harmony("net.dfgHiatus.AutoImageResize");
            harmony.PatchAll();
        }

        [HarmonyPatch(typeof(ImageImporter), "ImportImage")]
        class ModNameGoesHerePatch
        {
            public static bool Prefix(string path, ref Task __result, Slot targetSlot, float3? forward, StereoLayout stereoLayout, ImageProjection projection, bool setupScreenshotMetadata, bool addCollider)
            {
                Uri uri = new Uri(path);
                Image image = null;
                bool invalidImage = false;

                // Local file import vs URL import. Don't resize gifs!
                if (uri.Scheme == "file" && (!string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase)))
                {
                    Debug("GIF Detected!");
                    image = Image.FromStream(File.OpenRead(path));
                    invalidImage = true;
                }
                else if (uri.Scheme == "http" || uri.Scheme == "https")
                {
                    Debug("GIF Detected!");
                    var client = new System.Net.WebClient();
                    image = Image.FromStream(client.OpenRead(uri));
                    var type = client.ResponseHeaders.Get("content-type");
                    if (type == "image/gif")
                    {
                        invalidImage = true;
                    }
                }

                string imgPath = Path.Combine(Engine.Current.AppPath, "nml_mods", "tmp_resize.png");
                // Also don't resize a non-square image that isn't a power of 2
                if (!invalidImage &&
                    ((int) image.PhysicalDimension.Width > MAX_SIZE) &&
                    MathX.IsPowerOfTwo(image.Width) &&
                    (image.Width == image.Height)
                    )
                {
                    Debug("Imported image is a GIF or not a power of two. Image will not be resized.");
                    image?.Dispose();
                    return true;
                }
                else
                {
                    Debug("Saving Bitmap...");
                    var bitmap2D = Bitmap2D.LoadRaw(uri.ToString());
                    bitmap2D = bitmap2D.GetRescaled(MAX_SIZE, new bool?(false));
                    bitmap2D.Save(imgPath);
                    Debug($"Image saved as {imgPath}");
                }

                __result = targetSlot.StartTask(async delegate ()
                {
                    await default(ToBackground);

                    LocalDB localDB = targetSlot.World.Engine.LocalDB;
                    Debug("Loading image URI from DB. URI" + imgPath);
                    Uri localUri = await localDB.ImportLocalAssetAsync(imgPath, LocalDB.ImportLocation.Copy).ConfigureAwait(continueOnCapturedContext: false);
                    Debug("Loading checks out.");

                    File.Delete(imgPath);
                    Debug("Deleted File");

                    await default(ToWorld);

                    Debug("Setting up comps...");
                    targetSlot.Name = Path.GetFileNameWithoutExtension(imgPath);
                    if (forward.HasValue)
                    {
                        float3 from = forward.Value;
                        float3 to = float3.Forward;
                        targetSlot.LocalRotation = floatQ.FromToRotation(in from, in to);
                    }
                    StaticTexture2D tex = targetSlot.AttachComponent<StaticTexture2D>();
                    tex.URL.Value = localUri;
                    ImageImporter.SetupTextureProxyComponents(targetSlot, tex, stereoLayout, projection, setupScreenshotMetadata);
                    if (projection != 0)
                    {
                        ImageImporter.Create360Sphere(targetSlot, tex, stereoLayout, projection, addCollider);
                    }
                    else
                    {
                        while (!tex.IsAssetAvailable)
                        {
                            await default(NextUpdate);
                        }
                        ImageImporter.CreateQuad(targetSlot, tex, stereoLayout, addCollider);
                    }
                    if (setupScreenshotMetadata)
                    {
                        targetSlot.GetComponentInChildren<PhotoMetadata>()?.NotifyOfScreenshot();
                    }

                    QuadMesh _QuadMesh = targetSlot.GetComponent<QuadMesh>();
                    _QuadMesh.Size.Value = new float2(MAX_SIZE, MAX_SIZE).Normalized;

                });
                return false;
            }
        }
    }
}