using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Steamworks;

internal static class Program
{
    const uint App = 294100;
    const ulong Item = 3808896476;
    static bool done;
    static int exitCode = 1;
    static CallResult<SubmitItemUpdateResult_t> submit;
    static CallResult<SteamUGCQueryCompleted_t> query;

    static int Main(string[] args)
    {
        if (args.Length != 1 || (args[0] != "--query" && args[0] != "--publish" && args[0] != "--preview"))
            throw new ArgumentException("Use --query, --publish or --preview");
        if (!SteamAPI.Init())
        {
            Console.Error.WriteLine("SteamAPI.Init failed");
            return 2;
        }
        try
        {
            Console.WriteLine("Steam initialized as " + SteamFriends.GetPersonaName() + " (" + SteamUser.GetSteamID() + ")");
            if (args[0] == "--publish") Publish(); else if (args[0] == "--preview") PublishPreview(); else Query();
            var timer = Stopwatch.StartNew();
            while (!done && timer.Elapsed < TimeSpan.FromMinutes(5))
            {
                SteamAPI.RunCallbacks();
                Thread.Sleep(50);
            }
            if (!done) Console.Error.WriteLine("Timed out waiting for Steam callback");
            return done ? exitCode : 3;
        }
        finally { SteamAPI.Shutdown(); }
    }

    static string Root => Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../ModReleases/SteamUpload/SeamlessAncientBunkers-0.5.2"));

    static void PublishPreview()
    {
        var preview = Path.Combine(Root, "About/Preview.png");
        if (!File.Exists(preview)) throw new Exception("Workshop preview is missing");
        var handle = SteamUGC.StartItemUpdate(new AppId_t(App), new PublishedFileId_t(Item));
        Check(SteamUGC.SetItemPreview(handle, preview), "preview");
        submit = CallResult<SubmitItemUpdateResult_t>.Create((result, ioFailure) =>
        {
            Console.WriteLine("PREVIEW SUBMIT result=" + result.m_eResult + " io=" + ioFailure + " legal=" + result.m_bUserNeedsToAcceptWorkshopLegalAgreement + " id=" + result.m_nPublishedFileId);
            if (ioFailure || result.m_eResult != EResult.k_EResultOK || result.m_nPublishedFileId.m_PublishedFileId != Item)
            {
                exitCode = 4; done = true; return;
            }
            Query();
        });
        submit.Set(SteamUGC.SubmitItemUpdate(handle, "Updated Workshop preview artwork."));
    }

    static void Publish()
    {
        var marker = Path.Combine(Root, "RELEASE-READY.txt");
        var description = Path.Combine(Root, "STEAM-DESCRIPTION.txt");
        var preview = Path.Combine(Root, "About/Preview.png");
        if (!File.Exists(marker) || !File.ReadAllText(marker).Contains("Version 0.5.2")) throw new Exception("Reviewed 0.5.2 release marker is missing");
        if (!File.Exists(description) || !File.ReadAllText(description).Contains("NORMAL ORDERS ACROSS LEVELS")) throw new Exception("0.5.2 Workshop description is missing");
        if (!File.Exists(preview)) throw new Exception("Workshop preview is missing");
        var handle = SteamUGC.StartItemUpdate(new AppId_t(App), new PublishedFileId_t(Item));
        Check(SteamUGC.SetItemContent(handle, Root), "content");
        Check(SteamUGC.SetItemPreview(handle, preview), "preview");
        Check(SteamUGC.SetItemDescription(handle, File.ReadAllText(description)), "description");
        Check(SteamUGC.SetItemVisibility(handle, ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic), "visibility");
        submit = CallResult<SubmitItemUpdateResult_t>.Create((result, ioFailure) =>
        {
            Console.WriteLine("SUBMIT result=" + result.m_eResult + " io=" + ioFailure + " legal=" + result.m_bUserNeedsToAcceptWorkshopLegalAgreement + " id=" + result.m_nPublishedFileId);
            if (ioFailure || result.m_eResult != EResult.k_EResultOK || result.m_nPublishedFileId.m_PublishedFileId != Item)
            {
                exitCode = 4; done = true; return;
            }
            Query();
        });
        submit.Set(SteamUGC.SubmitItemUpdate(handle, "0.5.2: Fix false colonist-bed shortages across connected floors with the BunkBeds replacement bundled in Vanilla Gravship Expanded. Preserve bunk sleeping slots, medical exclusions and genuine shortage warnings. Verified with the Femageddom save and installed gameplay mods."));
    }

    static void Query()
    {
        var handle = SteamUGC.CreateQueryUGCDetailsRequest(new[] { new PublishedFileId_t(Item) }, 1);
        SteamUGC.SetReturnLongDescription(handle, true);
        SteamUGC.SetAllowCachedResponse(handle, 0);
        query = CallResult<SteamUGCQueryCompleted_t>.Create((result, ioFailure) =>
        {
            try
            {
                if (ioFailure || result.m_eResult != EResult.k_EResultOK || !SteamUGC.GetQueryUGCResult(result.m_handle, 0, out var details))
                {
                    Console.Error.WriteLine("QUERY failed result=" + result.m_eResult + " io=" + ioFailure);
                    exitCode = 5; return;
                }
                bool version = details.m_rgchDescription.Contains("Version 0.5.2");
                if (SteamUGC.GetQueryUGCPreviewURL(result.m_handle, 0, out var previewUrl, 2048))
                    Console.WriteLine("PREVIEW URL=" + previewUrl);
                bool orders = details.m_rgchDescription.Contains("NORMAL ORDERS ACROSS LEVELS");
                bool drafted = details.m_rgchDescription.Contains("Drafted colonists can use Go here across levels");
                Console.WriteLine("VERIFIED id=" + details.m_nPublishedFileId + " title=" + details.m_rgchTitle + " visibility=" + details.m_eVisibility + " bytes=" + details.m_nFileSize + " version=" + version + " orders=" + orders + " drafted=" + drafted);
                exitCode = details.m_nPublishedFileId.m_PublishedFileId == Item && version && orders && drafted && details.m_eVisibility == ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic ? 0 : 6;
            }
            finally { SteamUGC.ReleaseQueryUGCRequest(result.m_handle); done = true; }
        });
        query.Set(SteamUGC.SendQueryUGCRequest(handle));
    }

    static void Check(bool value, string field)
    {
        if (!value) throw new Exception("Steam rejected " + field);
    }
}



