﻿// Copyright 2020-2021 Andreas Atteneder
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//

using System;
using System.Collections.Generic;
using UnityEditor;
#if UNITY_2020_2_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif
using UnityEngine;
using Object = UnityEngine.Object;

namespace GLTFast.Editor {

    [ScriptedImporter(1,new [] {"gltf","glb"})] 
    public class GltfImporter : ScriptedImporter {

        [SerializeField]
        ImportSettings importSettings;
        
        [SerializeField]
        GltfAssetDependency[] assetDependencies;
        
        [SerializeField]
        LogItem[] reportItems;
        
        GltfImport m_Gltf;

        HashSet<string> m_ImportedNames;
        HashSet<Object> m_ImportedObjects;
        
        // static string[] GatherDependenciesFromSourceFile(string path) {
        //     // Called before actual import for each changed asset that is imported by this importer type
        //     // Extract the dependencies for the asset specified in path.
        //     // For asset dependencies that are discovered, return them in the string array, where the string is the path to asset
        //     
        //     // TODO: Texture files with relative URIs should be included here
        //     return null;
        // }
        
        public override void OnImportAsset(AssetImportContext ctx) {

            reportItems = null;

            var downloadProvider = new EditorDownloadProvider();
            var logger = new CollectingLogger();
            
            m_Gltf = new GltfImport(
                downloadProvider,
                new UninterruptedDeferAgent(),
                null,
                logger
                );

            if (importSettings == null) {
                // Design-time import specific settings
                importSettings = new ImportSettings {
                    // Avoid naming conflicts by default
                    nodeNameMethod = ImportSettings.NameImportMethod.OriginalUnique
                };
            }
            
            var success = AsyncHelpers.RunSync<bool>(() => m_Gltf.Load(ctx.assetPath,importSettings));

            GameObjectInstantiator instantiator = null;
            CollectingLogger instantiationLogger = null;
            if (success) {
                m_ImportedNames = new HashSet<string>();
                m_ImportedObjects = new HashSet<Object>();
                
                var go = new GameObject("root");
                instantiationLogger = new CollectingLogger();
                instantiator = new GameObjectInstantiator(m_Gltf, go.transform, instantiationLogger);
                for (var sceneIndex = 0; sceneIndex < m_Gltf.sceneCount; sceneIndex++) {
                    success = m_Gltf.InstantiateScene(instantiator,sceneIndex);
                    if (!success) break;
                    var sceneTransform = go.transform.GetChild(sceneIndex);
                    var sceneGo = sceneTransform.gameObject;
                    AddObjectToAsset(ctx,$"scenes/{sceneGo.name}", sceneGo);
                    if (sceneIndex == m_Gltf.defaultSceneIndex) {
                        ctx.SetMainObject(sceneGo);
                    }
                }
                
                for (var i = 0; i < m_Gltf.textureCount; i++) {
                    var texture = m_Gltf.GetTexture(i);
                    if (texture != null) {
                        AddObjectToAsset(ctx, $"textures/{texture.name}", texture);
                    }
                }
                
                for (var i = 0; i < m_Gltf.materialCount; i++) {
                    var mat = m_Gltf.GetMaterial(i);
                    AddObjectToAsset(ctx, $"materials/{mat.name}", mat);
                }

                if (m_Gltf.defaultMaterial != null) {
                    // If a default/fallback material was created, import it as well'
                    // to avoid (pink) objects without materials
                    var mat = m_Gltf.defaultMaterial;
                    AddObjectToAsset(ctx, $"materials/{mat.name}", mat);
                }
                
                var meshes = m_Gltf.GetMeshes();
                if (meshes != null) {
                    foreach (var mesh in meshes) {
                        AddObjectToAsset(ctx, $"meshes/{mesh.name}", mesh);
                    }
                }
                
                var clips = m_Gltf.GetAnimationClips();
                if (clips != null) {
                    foreach (var animationClip in clips) {
                        AddObjectToAsset(ctx, $"animations/{animationClip.name}", animationClip);
                    }
                }
                
                m_ImportedNames = null;
                m_ImportedObjects = null;
            }

            var deps = new List<GltfAssetDependency>();
            for (var index = 0; index < downloadProvider.assetDependencies.Count; index++) {
                var dependency = downloadProvider.assetDependencies[index];
                if (ctx.assetPath == dependency.originalUri) {
                    // Skip original gltf/glb file
                    continue;
                }

                var guid = AssetDatabase.AssetPathToGUID(dependency.originalUri);
                if (!string.IsNullOrEmpty(guid)) {
                    dependency.assetPath = dependency.originalUri;
                    ctx.DependsOnSourceAsset(dependency.assetPath);
                }

                deps.Add(dependency);
            }

            assetDependencies = deps.ToArray();

            var reportItemList = new List<LogItem>();
            reportItemList.AddRange(logger.items);
            if (instantiationLogger?.items != null) {
                reportItemList.AddRange(instantiationLogger.items);
            }
            reportItems = reportItemList.ToArray();
        }

        void AddObjectToAsset(AssetImportContext ctx, string originalName, Object obj) {
            if (m_ImportedObjects.Contains(obj)) {
                return;
            }
            var uniqueAssetName = GetUniqueAssetName(originalName);
            ctx.AddObjectToAsset(uniqueAssetName, obj);
            m_ImportedNames.Add(uniqueAssetName);
            m_ImportedObjects.Add(obj);
        }

        string GetUniqueAssetName(string originalName) {
            if (string.IsNullOrWhiteSpace(originalName)) {
                originalName = "Asset";
            }
            if(m_ImportedNames.Contains(originalName)) {
                var i = 0;
                string extName;
                do {
                    extName = $"{originalName}_{i++}";
                } while (m_ImportedNames.Contains(extName));
                return extName;
            }
            return originalName;
        }
    }
}