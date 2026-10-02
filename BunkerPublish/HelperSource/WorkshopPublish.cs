using System;
using System.IO;
using HarmonyLib;
using Steamworks;
using UnityEngine;
using Verse;
using Verse.Steam;
namespace SABSaveProbe
{
 // Local release helper, excluded from both the release and Workshop content.
 [StaticConstructorOnStartup]
 public static class WorkshopPublish
 {
  static bool Enabled=>GenCommandLine.CommandLineArgPassed("sab-publish-workshop");
  static bool started;static CallResult<SteamUGCQueryCompleted_t> queryResult;
  static CallResult<AddUGCDependencyResult_t> dependencyResult;
  static WorkshopPublish()
  {
   if(!Enabled&&!GenCommandLine.CommandLineArgPassed("sab-workshop-preflight")&&!GenCommandLine.CommandLineArgPassed("sab-workshop-query")&&!GenCommandLine.CommandLineArgPassed("sab-workshop-finalize"))return;
   new Harmony("colet.sab.workshopstart").Patch(AccessTools.Method(typeof(Root_Entry),nameof(Root_Entry.Update)),postfix:new HarmonyMethod(typeof(WorkshopPublish),nameof(Tick)));
  }
  public static void Tick()
  {
   if(started||Find.WindowStack==null||LongEventHandler.AnyEventNowOrWaiting)return;started=true;Application.runInBackground=true;
   {
    Log.Message("[SAB WORKSHOP] Steam initialized="+SteamManager.Initialized);
    if(GenCommandLine.CommandLineArgPassed("sab-workshop-finalize")&&SteamManager.Initialized)
    {
     dependencyResult=CallResult<AddUGCDependencyResult_t>.Create((r,io)=>{Log.Message("[SAB WORKSHOP] HARMONY dependency="+r.m_eResult+" io="+io);Verify();});
     dependencyResult.Set(SteamUGC.AddDependency(new PublishedFileId_t(3808896476),new PublishedFileId_t(2009463077)));return;
    }
    if(GenCommandLine.CommandLineArgPassed("sab-workshop-query")&&SteamManager.Initialized)
    {
     var handle=SteamUGC.CreateQueryUserUGCRequest(SteamUser.GetSteamID().GetAccountID(),EUserUGCList.k_EUserUGCList_Published,EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items,EUserUGCListSortOrder.k_EUserUGCListSortOrder_CreationOrderDesc,SteamUtils.GetAppID(),SteamUtils.GetAppID(),1);
     queryResult=CallResult<SteamUGCQueryCompleted_t>.Create((r,io)=>{Log.Message("[SAB WORKSHOP] QUERY "+r.m_eResult+" io="+io+" count="+r.m_unNumResultsReturned);for(uint n=0;n<r.m_unNumResultsReturned;n++)if(SteamUGC.GetQueryUGCResult(r.m_handle,n,out var item))Log.Message("[SAB WORKSHOP] ITEM id="+item.m_nPublishedFileId+" title="+item.m_rgchTitle+" created="+item.m_rtimeCreated+" bytes="+item.m_nFileSize+" visibility="+item.m_eVisibility);SteamUGC.ReleaseQueryUGCRequest(r.m_handle);Application.Quit();});queryResult.Set(SteamUGC.SendQueryUGCRequest(handle));return;
    }
    if(!Enabled||!SteamManager.Initialized){Application.Quit();return;}
    try
    {
     var path=Path.GetFullPath(Path.Combine(Application.dataPath,"../ModReleases/SteamUpload/SeamlessAncientBunkers"));
     // Resolve from the Unity game data directory.
     if(!File.Exists(Path.Combine(path,"RELEASE-READY.txt")))throw new Exception("Reviewed release marker is missing: "+path);
     var mod=new ModMetaData(path);
     if(mod.PackageIdPlayerFacing!="colet.seamlessancientbunkers")throw new Exception("Unexpected upload package");
     var harmony=new Harmony("colet.sab.workshoprelease");
     harmony.Patch(AccessTools.Method(typeof(Workshop),"SetWorkshopItemDataFrom"),postfix:new HarmonyMethod(typeof(WorkshopPublish),nameof(SetData)));
     harmony.Patch(AccessTools.Method(typeof(Workshop),"OnItemSubmitted"),postfix:new HarmonyMethod(typeof(WorkshopPublish),nameof(Submitted)));
     Prefs.LogVerbose=true;
     AccessTools.Method(typeof(Workshop),"Upload").Invoke(null,new object[]{mod});
    }catch(Exception e){Log.Error("[SAB WORKSHOP] FAILED "+e);Application.Quit();}
   }
  }
  public static void Verify()
  {
   var handle=SteamUGC.CreateQueryUGCDetailsRequest(new[]{new PublishedFileId_t(3808896476)},1);
   SteamUGC.SetReturnLongDescription(handle,true);
   queryResult=CallResult<SteamUGCQueryCompleted_t>.Create((r,io)=>
   {
    if(!io&&r.m_eResult==EResult.k_EResultOK&&SteamUGC.GetQueryUGCResult(r.m_handle,0,out var item))
    {
     SteamUGC.GetQueryUGCPreviewURL(r.m_handle,0,out var preview,2048);
     Log.Message("[SAB WORKSHOP] VERIFIED id="+item.m_nPublishedFileId+" title="+item.m_rgchTitle+" visibility="+item.m_eVisibility+" bytes="+item.m_nFileSize+" preview="+preview+" descriptionVersion="+item.m_rgchDescription.Contains("Version 0.4.3")+" linkedOrders="+item.m_rgchDescription.Contains("NORMAL ORDERS ACROSS LEVELS")+" draftedMove="+item.m_rgchDescription.Contains("Drafted colonists can use Go here across levels")+" utilityInstructions="+item.m_rgchDescription.Contains("ELECTRICITY AND PLUMBING THROUGH ENTRANCES"));
    }
    else Log.Error("[SAB WORKSHOP] Verification query failed "+r.m_eResult);
    SteamUGC.ReleaseQueryUGCRequest(r.m_handle);Application.Quit();
   });queryResult.Set(SteamUGC.SendQueryUGCRequest(handle));
  }
  public static void SetData(UGCUpdateHandle_t updateHandle,WorkshopItemHook hook)
  {
   if(!SteamUGC.SetItemDescription(updateHandle,hook.Description)||!SteamUGC.SetItemVisibility(updateHandle,ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic))throw new Exception("Steam rejected description or public visibility");
  }
  public static void Submitted(SubmitItemUpdateResult_t result,bool IOFailure)
  {
   Log.Message("[SAB WORKSHOP] SUBMIT result="+result.m_eResult+" IOFailure="+IOFailure+" legalAgreementRequired="+result.m_bUserNeedsToAcceptWorkshopLegalAgreement+" id="+result.m_nPublishedFileId);
   if(!IOFailure&&result.m_eResult==EResult.k_EResultOK)Verify();else Application.Quit();
  }
 }
}


