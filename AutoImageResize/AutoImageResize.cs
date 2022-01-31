using HarmonyLib;
using NeosModLoader;
using FrooxEngine;
using System.Threading.Tasks;
using BaseX;
using CodeX;
using System;
using System.IO;

namespace ModNameGoesHere
{
    public class AutoImageResize : NeosMod
    {
        [AutoRegisterConfigKey]
        public static ModConfigurationKey<int> MAX_SIZE_KEY = new ModConfigurationKey<int>("max_size", "Maximum size for Square, Power-Of-2 Textures", () => 2048);
        public static ModConfiguration config;

        public override string Name => "AutoImageResize";
        public override string Author => "dfgHiatus";
        public override string Version => "1.0.0";
        public override string Link => "https://github.com/dfgHiatus/AutoImageResize/";
        public override void OnEngineInit()
        {
            config = GetConfiguration();
            Harmony harmony = new Harmony("net.dfgHiatus.AutoImageResize");
            harmony.PatchAll();
        }

        [HarmonyPatch(typeof(ImageImporter), "ImportImage")]
        class ModNameGoesHerePatch
        {
            public static bool Prefix(string path, ref Task __result, Slot targetSlot, float3? forward, StereoLayout stereoLayout, ImageProjection projection, bool setupScreenshotMetadata, bool addCollider)
            {
                var bitmap2D = Bitmap2D.Load(path, true, true, CodeX.AlphaHandling.KeepOriginal, int.MaxValue, 1f);
                string imgPath = Path.Combine(Engine.Current.AppPath, "nml_mods", "tmp_resize.png");

                // Also don't resize a non-square image that isn't a power of 2
                if (bitmap2D != null &&
                    bitmap2D.Size.X > config.GetValue(MAX_SIZE_KEY) &&
                    MathX.IsPowerOfTwo(bitmap2D.Size.X) &&
                    bitmap2D.Size.X == bitmap2D.Size.Y
                    )
                {
                    __result = targetSlot.StartTask(async delegate ()
                    {
                        await default(ToBackground);

                        Debug("Saving Bitmap...");
                        bitmap2D = bitmap2D.GetRescaled(config.GetValue(MAX_SIZE_KEY), new bool?(false));
                        bitmap2D.Save(imgPath);
                        Debug($"Image saved as {imgPath}");

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
                        _QuadMesh.Size.Value = new float2(config.GetValue(MAX_SIZE_KEY), config.GetValue(MAX_SIZE_KEY)).Normalized;

                    });
                    return false;
                }
                else
                {
                    Debug("Imported image is not a square power of two. Image will not be resized.");
                    return true;
                }
            }
        }
    }
}