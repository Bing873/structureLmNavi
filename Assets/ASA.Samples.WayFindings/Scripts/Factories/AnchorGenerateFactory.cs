// Copyright (c) 2020 Takahiro Miyaura
// Released under the MIT license
// http://opensource.org/licenses/mit-license.php

using Com.Reseul.ASA.Samples.WayFindings.Anchors;
using UnityEngine;

namespace Com.Reseul.ASA.Samples.WayFindings.Factories
{
    /// <summary>
    ///     各種アンカーを生成するクラス
    /// </summary>
    public class AnchorGenerateFactory : MonoBehaviour
    {
        private static AnchorGenerateFactory instanceGenerate;

    #region Public Static Methods

        /// <summary>
        ///     このクラスのインスタンスを取得します。
        /// </summary>
        private static AnchorGenerateFactory InstanceObje
        {
            get
            {
                if (instanceGenerate == null)
                {
                    var module = FindObjectsOfType<AnchorGenerateFactory>();
                    if (module.Length == 1)
                    {
                        instanceGenerate = module[0];
                    }
                    else
                    {
                        Debug.LogWarning(
                            "Not found an existing Dialog in your scene. The Dialog requires only one.");
                    }
                }

                return instanceGenerate;
            }
        }

    #endregion

    #region Inspector Properites

        [SerializeField]
        [Tooltip("Set DestinationPointAnchor prefab for create instance.")]
        private DestinationPointAnchor destinationPointAnchor = null;

        [SerializeField]
        [Tooltip("Set SettingPointAnchor prefab for create instance.")]
        private SettingPointAnchor settingsPointAnchor = null;

        [SerializeField]
        [Tooltip("Set VSELandmarkAnchor prefab for create instance.")]
        private SettingPointAnchor VSEPointAnchor = null;

        [SerializeField]
        [Tooltip("Set SFLandmarkAnchor prefab for create instance.")]
        private SettingPointAnchor SFPointAnchor = null;

        [SerializeField]
        [Tooltip("Set DFLandmarkAnchor prefab for create instance.")]
        private SettingPointAnchor DFPointAnchor = null;

        [SerializeField]
        [Tooltip("Set VSELandmarkAnchor prefab for create wayfinding instance.")]
        private DestinationPointAnchor VSEDestPointAnchor = null;

        [SerializeField]
        [Tooltip("Set SFLandmarkAnchor prefab for create wayfinding instance.")]
        private DestinationPointAnchor SFDestPointAnchor = null;

        [SerializeField]
        [Tooltip("Set DFLandmarkAnchor prefab for create wayfinding instance.")]
        private DestinationPointAnchor DFDestPointAnchor = null;
        #endregion

        #region Public Static Methods

        /// <summary>
        ///     経路設定用のアンカーオブジェクトを生成します。
        /// </summary>
        /// <param name="mode"></param>
        /// <returns></returns>
        public static SettingPointAnchor GenerateSettingsPointAnchor(SettingPointAnchor.AnchorMode mode)
        {
            
            var obj = Instantiate(
               mode == SettingPointAnchor.AnchorMode.VSE
               ? InstanceObje.VSEPointAnchor
               : mode == SettingPointAnchor.AnchorMode.SF
               ? InstanceObje.SFPointAnchor
               : mode == SettingPointAnchor.AnchorMode.DF
               ? InstanceObje.DFPointAnchor
               : InstanceObje.settingsPointAnchor);
            
            obj.SetAnchorMode(mode);
            return obj;
        }

        /// <summary>
        ///     経路案内用のアンカーオブジェクトを生成します。
        /// </summary>
        /// <param name="parent"></param>
        /// <returns></returns>
        public static DestinationPointAnchor GenerateDestinationPointAnchor(string destmode, Transform parent = null)
        {
            var obj = Instantiate(
                destmode == ((SettingPointAnchor.AnchorMode)2).ToString()
               ? InstanceObje.VSEDestPointAnchor
               : destmode == ((SettingPointAnchor.AnchorMode)3).ToString()
               ? InstanceObje.SFDestPointAnchor
               : destmode == ((SettingPointAnchor.AnchorMode)4).ToString()
               ? InstanceObje.DFDestPointAnchor
               : InstanceObje.destinationPointAnchor);
            obj.transform.parent = parent;
            return obj;
        }

    #endregion
    }
}