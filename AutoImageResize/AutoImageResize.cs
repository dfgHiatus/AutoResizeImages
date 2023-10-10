using HarmonyLib;
using ResoniteModLoader;
using FrooxEngine;
using System.Threading.Tasks;
using Elements.Core;
using Elements.Assets;
using System;
using System.IO;

namespace AutoImageResize;

public class AutoImageResize : ResoniteMod
{
    [AutoRegisterConfigKey]
    public readonly static ModConfigurationKey<int> MAX_SIZE_KEY = 
        new("max_size", "Maximum size for Square, Power-Of-2 Textures", () => 2048);

    [AutoRegisterConfigKey]
    public readonly static ModConfigurationKey<bool> IS_ENABLED = new
        ("is_enabled", "Enabled", () => true);

    public static ModConfiguration config;

    public override string Name => "AutoImageResize";
    public override string Author => "dfgHiatus";
    public override string Version => "2.0.0";
    public override string Link => "https://github.com/dfgHiatus/AutoImageResize/";
    public override void OnEngineInit()
    {
        config = GetConfiguration();
        new Harmony("net.dfgHiatus.AutoImageResize").PatchAll();
    }

    [HarmonyPatch(typeof(ImageImporter), "ImportImage")]
    public class AutoImageResizePatch
    {
        public static bool Prefix(string path, ref Task __result, Slot targetSlot, float3? forward, StereoLayout stereoLayout, ImageProjection projection, bool setupScreenshotMetadata, bool addCollider)
        {
            var bitmap2D = Bitmap2D.Load(path, true, Elements.Assets.AlphaHandling.KeepOriginal, int.MaxValue, 1f);
            var imgPath = Path.Combine(Path.GetTempPath(), $"{Path.GetTempFileName()}.png");

            // Also don't resize a non-square image that isn't a power of 2
            if (bitmap2D != null &&
                bitmap2D.Size.X > config.GetValue(MAX_SIZE_KEY) &&
                MathX.IsPowerOfTwo(bitmap2D.Size.X) &&
                bitmap2D.Size.X == bitmap2D.Size.Y
                && config.GetValue(IS_ENABLED)
                )
            {
                __result = targetSlot.StartTask(async delegate ()
                {
                    await default(ToBackground);
                    bitmap2D = bitmap2D.GetRescaled(config.GetValue(MAX_SIZE_KEY), new bool?(false));
                    bitmap2D.Save(imgPath);

                    LocalDB localDB = targetSlot.World.Engine.LocalDB;
                    Uri localUri = await localDB.ImportLocalAssetAsync(imgPath, LocalDB.ImportLocation.Copy).
                        ConfigureAwait(continueOnCapturedContext: false);
                    File.Delete(imgPath);
                    await default(ToWorld);

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

            return true;
        }
    }
}