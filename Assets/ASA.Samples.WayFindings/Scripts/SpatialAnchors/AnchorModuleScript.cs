// Copyright (c) 2020 Takahiro Miyaura
// Released under the MIT license
// http://opensource.org/licenses/mit-license.php

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.SpatialAnchors;
using Microsoft.Azure.SpatialAnchors.Unity;
using Microsoft.MixedReality.Toolkit;
using UnityEngine;

namespace Com.Reseul.ASA.Samples.WayFindings.SpatialAnchors
{
    public class AnchorModuleScript : MonoBehaviour, IAnchorModuleScript
    {
        /// <summary>
        ///     排他制御用オブジェクト
        /// </summary>
        private static object lockObj = new object();

        /// <summary>
        ///     Unityのメインスレッド上で実行したい処理を格納するキュー
        /// </summary>
        private readonly Queue<Action> dispatchQueue = new Queue<Action>();

        /// <summary>
        ///     Azure Spatial Anchorsから取得したAnchorの情報を格納するDictionary
        /// </summary>
        private readonly Dictionary<string, CloudSpatialAnchor> locatedAnchors =
            new Dictionary<string, CloudSpatialAnchor>();

        /// <summary>
        ///     Azure Spatial Anchorsの検索時に設定する<see cref="AnchorLocateCriteria" />
        /// </summary>
        private AnchorLocateCriteria anchorLocateCriteria;

        /// <summary>
        ///     Azure Spatial Anchorsの管理クラス
        /// </summary>
        private SpatialAnchorManager cloudManager;

        /// <summary>
        ///     Azure Spatial Anchors検索時に利用する監視クラス
        /// </summary>
        private CloudSpatialAnchorWatcher currentWatcher;

        /// <summary>
        ///     Azure Spatial Anchorsのパラメータ：アンカー周辺を検索する際の探索範囲（単位:m）
        /// </summary>
        private float distanceInMeters;

        /// <summary>
        ///     Azure Spatial Anchorsのパラメータ：Spatial Anchor登録時のアンカーの寿命（単位:日）
        /// </summary>
        private int expiration;

        /// <summary>
        ///     特定のアンカーを中心に検索した際に見つかったアンカー一覧
        /// </summary>
        private List<string> findNearByAnchorIds = new List<string>();

        /// <summary>
        ///     Azure Spatial Anchorsのパラメータ：アンカー周辺の検索時に取得するアンカーの上限数
        /// </summary>
        private int maxResultCount;

        /// <summary>
        ///     アンカー取得後に実行する個別処理を持つコントローラクラス
        /// </summary>
        public IASACallBackManager CallBackManager { get; set; }

    #region Public Events

        /// <summary>
        ///     処理状況を出力するイベント
        /// </summary>
        public event AnchorModuleProxy.FeedbackDescription OnFeedbackDescription;

    #endregion

    #region Internal Methods and Coroutines

        /// <summary>
        ///     Unityのメインスレッド上で実行したい処理をキューに投入します。
        /// </summary>
        /// <param name="updateAction"></param>
        private void QueueOnUpdate(Action updateAction)
        {
            lock (dispatchQueue)
            {
                dispatchQueue.Enqueue(updateAction);
            }
        }

    #endregion

    #region Unity Lifecycle

        /// <summary>
        ///     初期化処理を実施します
        /// </summary>
        public void Start()
        {
            try
            {
                // Get the component for managing Azure Spatial Anchors.
                cloudManager = GetComponent<SpatialAnchorManager>();

                // Assign the event that occurs when you call the Azure Spatial Anchors service.
                // Event that occurs when anchor installation is completed based on the anchor information obtained from Azure Spatial Anchors
                cloudManager.AnchorLocated += CloudManager_AnchorLocated;

                // Event called that all anchor installation process obtained from Azure Spatial Anchors is completed
                cloudManager.LocateAnchorsCompleted += CloudManager_LocateAnchorsCompleted;

                // Instantiate a class that sets search criteria for Azure Spatial Anchors
                anchorLocateCriteria = new AnchorLocateCriteria();
            }
            catch (Exception e)
            {
                Debug.Log(e);
                throw;
            }
        }

        /// <summary>
        ///     フレーム毎に実行する処理を実施します。
        /// </summary>
        public void Update()
        {
            try
            {
                // Remove the process you want to execute on the main thread of Unity from the queue and start the process.
                lock (dispatchQueue)
                {
                    if (dispatchQueue.Count > 0)
                    {
                        dispatchQueue.Dequeue()();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log(e);
                throw;
            }
        }

        /// <summary>
        ///     オブジェクトの後処理（廃棄）を実施します。
        /// </summary>
        public void OnDestroy()
        {
            try
            {
                if (cloudManager != null && cloudManager.Session != null)
                {
                    cloudManager.DestroySession();
                }

                if (currentWatcher != null)
                {
                    currentWatcher.Stop();
                    currentWatcher = null;
                }
            }
            catch (Exception e)
            {
                Debug.Log(e);
                throw;
            }
        }

    #endregion

    #region Public Methods

        /// <summary>
        ///     Azure Spatial Anchorsサービスとの接続を行い、セションを開始します。
        /// </summary>
        /// <returns></returns>
        public async Task StartAzureSession()
        {
            try
            {
                Debug.Log("\nAnchorModuleScript.StartAzureSession()");

                OutputLog("Starting Azure session... please wait...");

                if (cloudManager.Session == null)
                {
                    // Creates a new session if one does not exist
                    await cloudManager.CreateSessionAsync();
                }

                // Starts the session if not already started
                await cloudManager.StartSessionAsync();

                OutputLog("Azure session started successfully");
            }
            catch (Exception e)
            {
                Debug.Log(e);
                throw;
            }
        }

        /// <summary>
        ///     Azure Spatial Anchorsサービスとの接続を停止します。
        /// </summary>
        /// <returns></returns>
        public async Task StopAzureSession()
        {
            try
            {
                Debug.Log("\nAnchorModuleScript.StopAzureSession()");

                OutputLog("Stopping Azure session... please wait...");

                // Stops any existing session
                cloudManager.StopSession();

                // Resets the current session if there is one, and waits for any active queries to be stopped
                await cloudManager.ResetSessionAsync();

                OutputLog("Azure session stopped successfully", isOverWrite: true);
            }
            catch (Exception e)
            {
                Debug.Log(e);
                throw;
            }
        }

        /// <summary>
        ///     Change the App Properties of Spatial Anchor obtained from Azure Spatial Anchors at once.
        ///     If the key already exists, replace it according to the value of the replace parameter and switch the appending to execute the process.
        /// </summary>
        /// <param name="key">AppPropertiesのキー</param>
        /// <param name="val">Value corresponding to the key</param>
        /// <param name="replace">true: overwrite, false: comma separated</param>
        public async void UpdatePropertiesAll(string key, string val, bool replace = true)
        {
            try
            {
                OutputLog("Trying to update AppProperties of Azure anchors");
                foreach (var info in locatedAnchors.Values)
                {
                    await UpdateProperties(info, key, val, replace);
                }
            }
            catch (Exception e)
            {
                Debug.Log(e);
                throw;
            }
        }

        /// <summary>
        ///    Add an anchor to the Azure Spatial Anchors service.
        /// </summary>
        /// <param name="theObject">Objects installed in real space to be registered as Spatial Anchor information</param>
        /// <param name="appProperties">Information to include in Spatial Anchor</param>
        /// <returns>AnchorId at the time of registration</returns>
        public async Task<string> CreateAzureAnchor(GameObject theObject, IDictionary<string, string> appProperties)
        {
            try
            {
                Debug.Log("\nAnchorModuleScript.CreateAzureAnchor()");

                OutputLog("Creating Azure anchor");

                // Configure the Native Anchor settings required to register the Azure Spatial Anchors service.
                theObject.CreateNativeAnchor();

                OutputLog("Creating local anchor");

                // Azure Spatial Anchors Prepare the Spatial Anchor information to register with the service.
                var localCloudAnchor = new CloudSpatialAnchor();

                // Stores information including Spatial Anchor.
                foreach (var key in appProperties.Keys)
                {
                    localCloudAnchor.AppProperties.Add(key, appProperties[key]);
                }


                // Pass a pointer to the Native Anchor.
                localCloudAnchor.LocalAnchor = theObject.FindNativeAnchor().GetPointer();

                // Check if Native Anchor is generated normally. If the generation fails, it will end.
                if (localCloudAnchor.LocalAnchor == IntPtr.Zero)
                {
                    OutputLog("Didn't get the local anchor...", LogType.Error);
                    return null;
                }

                Debug.Log("Local anchor created");

                // Set the life of the Spatial Anchor. Anchors will remain on the Azure Spatial Anchors service for this number of days.
                localCloudAnchor.Expiration = DateTimeOffset.Now.AddDays(expiration);

                OutputLog("Move your device to capture more environment data: 0%");

                // Make sure that the space features required for Spatial Anchor registration are sufficient.
                // RecommendedForCreateProgress is 100% collecting the necessary information (in the case of HoloLens, it will be finished in almost an instant)
                do
                {
                    await Task.Delay(330);
                    var createProgress = cloudManager.SessionStatus.RecommendedForCreateProgress;
                    QueueOnUpdate(() =>
                        OutputLog($"Move your device to capture more environment data: {createProgress:0%}",
                            isOverWrite: true));
                } while (!cloudManager.IsReadyForCreate);

                try
                {
                    OutputLog("Creating Azure anchor... please wait...");

                    // Try to register with Azure Spatial Anchors.
                    await cloudManager.CreateAnchorAsync(localCloudAnchor);

                    // If the registration is successful, the object containing the registration result (AnchorId) will be returned.
                    var success = localCloudAnchor != null;
                    if (success)
                    {
                        OutputLog($"Azure anchor with ID '{localCloudAnchor.Identifier}' created successfully");
                        locatedAnchors.Add(localCloudAnchor.Identifier, localCloudAnchor);
                        return localCloudAnchor.Identifier;
                    }

                    OutputLog("Failed to save cloud anchor to Azure", LogType.Error);
                }
                catch (Exception ex)
                {
                    Debug.Log(ex.ToString());
                }

                return null;
            }
            catch (Exception e)
            {
                Debug.Log(e);
                throw;
            }
        }

        /// <summary>
        ///     指定されたAnchorIdで登録されたAnchorを中心に他のアンカーが存在するか検索を実施します。
        /// </summary>
        /// <param name="anchorId">基準になるAnchorId</param>
        public void FindNearByAnchor(string anchorId)
        {
            try
            {
                anchorLocateCriteria.Identifiers = new string[0];
                Debug.Log("\nAnchorModuleScript.FindAzureAnchor()");
                OutputLog("Trying to find near by Azure anchor");

                // Check if the specified Anchor exists in the list of acquired Spatial Anchors managed by this class.
                if (!locatedAnchors.ContainsKey(anchorId))
                {
                    OutputLog($"Not found anchor.id:{anchorId}.", LogType.Error);
                    return;
                }

                // Set the conditions to search Azure Spatial Anchors.
                // Assign an instance of NearAnchorCriteria to Criteria to search around the anchor.
                anchorLocateCriteria.NearAnchor = new NearAnchorCriteria();

                // Set the information of the Anchor that will be the base point.
                anchorLocateCriteria.NearAnchor.SourceAnchor = locatedAnchors[anchorId];

                // Set the search range and the number of simultaneous detections
                anchorLocateCriteria.NearAnchor.DistanceInMeters = distanceInMeters;
                anchorLocateCriteria.NearAnchor.MaxResultCount = maxResultCount;

                // Set the rules for searching. Set AnyStrategy for peripheral search.
                anchorLocateCriteria.Strategy = LocateStrategy.AnyStrategy;

                Debug.Log(
                    $"Anchor locate criteria configured to Search Near by Azure anchor ID '{anchorLocateCriteria.NearAnchor.SourceAnchor.Identifier}'");

                // Start searching for anchors. This process is time consuming, so in Azure Spatial Anchors
                // Generate Watcher and perform asynchronous processing on another thread.
                // AnchorLocated event is generated sequentially from the information that Anchor search and placement is completed.
                // When all the acquired Spatial Anchor installation is completed, the LocatedAnchorsComplete event will be fired.
                if (cloudManager != null && cloudManager.Session != null)
                {
                    currentWatcher = cloudManager.Session.CreateWatcher(anchorLocateCriteria);
                    Debug.Log("Watcher created");
                    OutputLog("Looking for Azure anchor... please wait...");
                }
                else
                {
                    OutputLog("Attempt to create watcher failed, no session exists", LogType.Error);
                    currentWatcher = null;
                }
            }
            catch (Exception e)
            {
                Debug.Log(e);
                throw;
            }
        }

        /// <summary>
        ///     Gets the Spatial Anchor corresponding to the specified AnchorId from the Azure Spatial Anchors service.
        /// </summary>
        /// <param name="azureAnchorIds"></param>
        public void FindAzureAnchorById(params string[] azureAnchorIds)
        {
            try
            {
                Debug.Log("\nAnchorModuleScript.FindAzureAnchor()");

                OutputLog("Trying to find Azure anchor");

                var anchorsToFind = new List<string>();

                if (azureAnchorIds != null && azureAnchorIds.Length > 0)
                {
                    anchorsToFind.AddRange(azureAnchorIds);
                }
                else
                {
                    OutputLog("Current Azure anchor ID is empty", LogType.Error);
                    return;
                }

                // Set the criteria to search for Azure Spatial Anchors.
                anchorLocateCriteria = new AnchorLocateCriteria();

                // Sets the list of AnchorIds to search.
                anchorLocateCriteria.Identifiers = anchorsToFind.ToArray();
         
                // Set whether to use the local information as a cache when reacquiring the anchor information once acquired.
                // This time it bypasses the cache, so it will query Azure Spatial Anchors every time.
                anchorLocateCriteria.BypassCache = true;

                // Start searching for anchors. This process is time consuming, so in Azure Spatial Anchors
                // Generate Watcher and perform asynchronous processing on another thread.
                // AnchorLocated event is generated sequentially from the information that Anchor search and placement is completed.
                // When all the acquired Spatial Anchor installation is completed, the LocatedAnchorsComplete event will be fired.
                if (cloudManager != null && cloudManager.Session != null)
                {
                    currentWatcher = cloudManager.Session.CreateWatcher(anchorLocateCriteria);

                    Debug.Log("Watcher created");
                    OutputLog("Looking for Azure anchor... please wait...");
                }
                else
                {
                    OutputLog("Attempt to create watcher failed, no session exists", LogType.Error);

                    currentWatcher = null;
                }
            }
            catch (Exception e)
            {
                Debug.Log(e);
                throw;
            }
        }

        /// <summary>
        ///     Remove all acquired anchors from the Azure Spatial Anchors service.
        /// </summary>
        public async void DeleteAllAzureAnchor()
        {
            try
            {
                Debug.Log("\nAnchorModuleScript.DeleteAllAzureAnchor()");

                // Notify AnchorFeedbackScript
                OutputLog("Trying to find Azure anchor...");

                foreach (var AnchorInfo in locatedAnchors.Values)
                {
                    // Delete the Azure anchor with the ID specified off the server and locally
                    await cloudManager.DeleteAnchorAsync(AnchorInfo);
                }

                locatedAnchors.Clear();

                OutputLog("Trying to find Azure anchor...Successfully");
            }
            catch (Exception e)
            {
                Debug.Log(e);
                throw;
            }
        }

        /// <summary>
        ///     Set the controller to execute the Anchor generation process.
        /// </summary>
        /// <param name="iasaCallBackManager"></param>
        public void SetASACallBackManager(IASACallBackManager iasaCallBackManager)
        {
            CallBackManager = iasaCallBackManager;
        }

        /// <summary>
        ///     Sets the search range for Spatial Anchor.
        /// </summary>
        /// <param name="distanceInMeters">Search range (unit: m)</param>
        public void SetDistanceInMeters(float distanceInMeters)
        {
            this.distanceInMeters = distanceInMeters;
        }

        /// <summary>
        ///     Sets the life of the Spatial Anchor
        /// </summary>
        /// <param name="expiration">Anchor registration period (unit: days)</param>
        public void SetExpiration(int expiration)
        {
            this.expiration = expiration;
        }

        /// <summary>
        ///     Sets the number of simultaneous searches for Spatial Anchor
        /// </summary>
        /// <param name="distanceInMeters">検索数</param>
        public void SetMaxResultCount(int maxResultCount)
        {
            this.maxResultCount = maxResultCount;
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Changes the App Properties of the specified Spatial Anchor.
        ///     If the key already exists, replace it according to the value of the replace parameter and 
        ///     switch the appending to execute the process.
        /// </summary>
        /// <param name="currentCloudAnchor">Information on the Spatial Anchor to be changed</param>
        /// <param name="key">AppProperties key</param>
        /// <param name="val">Value corresponding to the key</param>
        /// <param name="replace">true: overwrite, false: comma separated</param>
        /// <returns></returns>
        private async Task UpdateProperties(CloudSpatialAnchor currentCloudAnchor, string key, string val,
            bool replace = true)
        {
            try
            {
                OutputLog($"anchor properties.id:{currentCloudAnchor.Identifier} -- key:{key},val:{val}....");
                if (currentCloudAnchor != null)
                {
                    if (currentCloudAnchor.AppProperties.ContainsKey(key))
                    {
                        if (replace || currentCloudAnchor.AppProperties[key].Length == 0)
                        {
                            currentCloudAnchor.AppProperties[key] = val;
                        }
                        else
                        {
                            currentCloudAnchor.AppProperties[key] = currentCloudAnchor.AppProperties[key] + "," + val;
                        }
                    }
                    else
                    {
                        currentCloudAnchor.AppProperties.Add(key, val);
                    }

                    // Start watching for Anchors
                    if (cloudManager != null && cloudManager.Session != null)
                    {
                        await cloudManager.Session.UpdateAnchorPropertiesAsync(currentCloudAnchor);
                        var result = await cloudManager.Session.GetAnchorPropertiesAsync(currentCloudAnchor.Identifier);

                        OutputLog(
                            $"anchor properties.id:{currentCloudAnchor.Identifier} -- key:{key},val:{val}....successfully",
                            isOverWrite: true);
                    }
                    else
                    {
                        OutputLog("Attempt to create watcher failed, no session exists", LogType.Error);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log(e);
                throw;
            }
        }

        /// <summary>
        ///     衆utputs the processing progress.
        /// </summary>
        /// <param name="message">message</param>
        /// <param name="logType">Output log type</param>
        /// <param name="isOverWrite">Overwrite previous message</param>
        /// <param name="isReset">Clear the message</param>
        private void OutputLog(string message, LogType logType = LogType.Log, bool isOverWrite = false,
            bool isReset = false)
        {
            try
            {
                OnFeedbackDescription?.Invoke(message, isOverWrite, isReset);
                switch (logType)
                {
                    case LogType.Log:
                        Debug.Log(message);
                        break;
                    case LogType.Error:
                        Debug.LogError(message);
                        break;
                    case LogType.Warning:
                        Debug.LogError(message);
                        break;
                    default:
                        Debug.Log(message);
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.Log(e);
                throw;
            }
        }

        #endregion

        #region Event Handlers

        /// <summary>
        ///     Process to be executed in the event that occurs when the installation of Spatial Anchor is completed
        /// </summary>
        /// <param name="sender">sender</param>
        /// <param name="args">args</param>
        private void CloudManager_AnchorLocated(object sender, AnchorLocatedEventArgs args)
        {
            try
            {
                // Processing is performed according to the condition of the installed anchor.
                if (args.Status == LocateAnchorStatus.Located || args.Status == LocateAnchorStatus.AlreadyTracked)
                {
                    // AppProperties will be empty when searching for Anchor with FindNearbyAnchors (bug?)
                    // For this reason, only the IDs of the anchors found by the FindNearbyAnchors search are aggregated and after all the placement is completed.
                    // Re-acquire with FindAzureAnchorById. This process is performed within CloudManager_LocateAnchorsCompleted.
                    if (IsNearbyMode())
                    {
                        var id = args.Anchor.Identifier;
                        QueueOnUpdate(() => Debug.Log($"Find near by Anchor id:{id}"));
                        lock (lockObj)
                        {
                            findNearByAnchorIds.Add(id);
                        }
                    }
                    else
                    {
                        QueueOnUpdate(() => Debug.Log("Anchor recognized as a possible Azure anchor"));
                        // Store the acquired Spatial Anchor information in the list.
                        lock (lockObj)
                        {
                            if (!locatedAnchors.ContainsKey(args.Anchor.Identifier))
                            {
                                locatedAnchors.Add(args.Anchor.Identifier, args.Anchor);
                            }
                        }

                        // Create a Unity object from the acquired Spatial Anchor information and place it in the correct position in the real space.
                        QueueOnUpdate(() =>
                        {
                            var currentCloudAnchor = args.Anchor;

                            Debug.Log("Azure anchor located successfully");

                            GameObject point = null;

                            // Calls the process to create a Unity object corresponding to Spatial Anchor.
                            if (CallBackManager != null && !CallBackManager.OnLocatedAnchorObject(
                                    currentCloudAnchor.Identifier,
                                    locatedAnchors[currentCloudAnchor.Identifier].AppProperties, out point))
                            {
                                return;
                            }

                            if (point == null)
                            {
                                OutputLog("Not Anchor Object", LogType.Error);
                                return;
                            }

                            point.SetActive(true);

                            // Notify AnchorFeedbackScript
                            OutputLog("Azure anchor located");
                            var anchorPose = Pose.identity;

#if UNITY_ANDROID || UNITY_IOS
                        anchorPose = currentCloudAnchor.GetPose();
#endif
                            OutputLog("Creating local anchor");
                            var cloudNativeAnchor = point.EnsureComponent<CloudNativeAnchor>();

                            // Place the Unity object in the correct position in real space.
                            if (currentCloudAnchor != null)
                            {
                                Debug.Log("Local anchor position successfully set to Azure anchor position");

                                // Generate a Native Anchor.
                                cloudNativeAnchor.CloudToNative(currentCloudAnchor);
                            }
                            else
                            {
                                cloudNativeAnchor.SetPose(anchorPose.position, anchorPose.rotation);
                            }
                        });
                    }
                }
                else
                {
                    QueueOnUpdate(() =>
                        OutputLog(
                            $"Attempt to locate Anchor with ID '{args.Identifier}' failed, locate anchor status was not 'Located' but '{args.Status}'",
                            LogType.Error));
                }
            }
            catch (Exception e)
            {
                Debug.Log(e);
                throw;
            }
        }

        /// <summary>
        ///    The process to be executed after the installation of all the searched Spatial Anchors is completed.
        /// </summary>
        /// <param name="sender">sender</param>
        /// <param name="args">args</param>
        private void CloudManager_LocateAnchorsCompleted(object sender, LocateAnchorsCompletedEventArgs args)
        {
            try
            {
                if (IsNearbyMode())
                {
                    // Because AppProperties information cannot be obtained when obtained with Nearby Anchor
                    // Hold the AnchorId of the Spatial Anchor that was once placed, and acquire the anchor again by specifying the ID.
                    QueueOnUpdate(() => OutputLog("Get the spatial anchors with Anchor App Properties."));
                    QueueOnUpdate(() => FindAzureAnchorById(findNearByAnchorIds.ToArray()));
                }
                else
                {
                    findNearByAnchorIds.Clear();
                    QueueOnUpdate(() => OutputLog("Locate Azure anchors Complete."));

                    if (!args.Cancelled)
                    {
                        // Execute after all the searched Spatial Anchors have been installed.
                        QueueOnUpdate(() => CallBackManager?.OnLocatedAnchorComplete());
                    }
                    else
                    {
                        QueueOnUpdate(() => OutputLog("Attempt to locate Anchor Complete failed.", LogType.Error));
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log(e);
                throw;
            }
        }

        /// <summary>
        ///     Check if the search is in Nearby Anchor.
        /// </summary>
        /// <returns>Search in Nearby Anchor is true</returns>
        private bool IsNearbyMode()
        {
            return anchorLocateCriteria?.NearAnchor != null;
        }

    #endregion
    }
}