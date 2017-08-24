﻿namespace SEToolbox.Interop
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Resources;
    using System.Text;
    using System.Xml;
    using Sandbox.Definitions;
    using SEToolbox.Support;
    using VRage;
    using VRage.Game;
    using VRage.ObjectBuilders;
    using VRage.Utils;
    using VRageMath;

    /// <summary>
    /// Helper api for accessing and interacting with Space Engineers content.
    /// </summary>
    public static class SpaceEngineersApi
    {
        #region Serializers

        public static T ReadSpaceEngineersFile<T>(Stream stream) where T : MyObjectBuilder_Base
        {
            T outObject;
            MyObjectBuilderSerializer.DeserializeXML<T>(stream, out outObject);
            return outObject;
        }

        public static bool TryReadSpaceEngineersFile<T>(string filename, out T outObject, out bool isCompressed, bool snapshot = false) where T : MyObjectBuilder_Base
        {
            isCompressed = false;

            if (File.Exists(filename))
            {
                var tempFilename = filename;

                if (snapshot)
                {
                    // Snapshot used for Report on Dedicated servers to prevent locking of the orginal file whilst reading it.
                    tempFilename = TempfileUtil.NewFilename();
                    File.Copy(filename, tempFilename);
                }

                using (var fileStream = new FileStream(tempFilename, FileMode.Open, FileAccess.Read))
                {
                    var b1 = fileStream.ReadByte();
                    var b2 = fileStream.ReadByte();
                    isCompressed = (b1 == 0x1f && b2 == 0x8b);
                }

                return MyObjectBuilderSerializer.DeserializeXML<T>(tempFilename, out outObject);
            }

            outObject = null;
            return false;
        }

        public static T Deserialize<T>(string xml) where T : MyObjectBuilder_Base
        {
            T outObject;
            using (var stream = new MemoryStream())
            {
                StreamWriter sw = new StreamWriter(stream);
                sw.Write(xml);
                sw.Flush();
                stream.Position = 0;

                MyObjectBuilderSerializer.DeserializeXML(stream, out outObject);
            }
            return outObject;
        }

        public static string Serialize<T>(MyObjectBuilder_Base item) where T : MyObjectBuilder_Base
        {
            using (var outStream = new MemoryStream())
            {
                if (MyObjectBuilderSerializer.SerializeXML(outStream, item))
                {
                    outStream.Position = 0;

                    StreamReader sw = new StreamReader(outStream);
                    return sw.ReadToEnd();
                }
            }
            return null;
        }

        public static bool WriteSpaceEngineersFile<T>(T myObject, string filename)
            where T : MyObjectBuilder_Base
        {
            bool ret;
            using (StreamWriter sw = new StreamWriter(filename))
            {
                ret = MyObjectBuilderSerializer.SerializeXML(sw.BaseStream, myObject);
                if (ret)
                {
                    var xmlTextWriter = new XmlTextWriter(sw.BaseStream, null);
                    xmlTextWriter.WriteString("\r\n");
                    xmlTextWriter.WriteComment(string.Format(" Saved '{0:o}' with SEToolbox version '{1}' ", DateTime.Now, GlobalSettings.GetAppVersion()));
                    xmlTextWriter.Flush();
                }
            }

            return true;
        }

        #endregion

        #region GenerateEntityId

        public static long GenerateEntityId(VRage.MyEntityIdentifier.ID_OBJECT_TYPE type)
        {
            return MyEntityIdentifier.AllocateId(type);
        }

        public static bool ValidateEntityType(VRage.MyEntityIdentifier.ID_OBJECT_TYPE type, long id)
        {
            return MyEntityIdentifier.GetIdObjectType(id) == type;
        }

        //public static long GenerateEntityId()
        //{
        //    // Not the offical SE way of generating IDs, but its fast and we don't have to worry about a random seed.
        //    var buffer = Guid.NewGuid().ToByteArray();
        //    return BitConverter.ToInt64(buffer, 0);
        //}

        #endregion

        #region FetchCubeBlockMass

        public static float FetchCubeBlockMass(MyObjectBuilderType typeId, MyCubeSize cubeSize, string subTypeid)
        {
            float mass = 0;

            var cubeBlockDefinition = GetCubeDefinition(typeId, cubeSize, subTypeid);

            if (cubeBlockDefinition != null)
            {
                return cubeBlockDefinition.Mass;
            }

            return mass;
        }

        public static void AccumulateCubeBlueprintRequirements(string subType, MyObjectBuilderType typeId, decimal amount, Dictionary<string, BlueprintRequirement> requirements, out TimeSpan timeTaken)
        {
            var time = new TimeSpan();
            var bp = SpaceEngineersApi.GetBlueprint(typeId, subType);

            if (bp != null && bp.Results != null && bp.Results.Length > 0)
            {
                foreach (var item in bp.Prerequisites)
                {
                    if (requirements.ContainsKey(item.Id.SubtypeName))
                    {
                        // append existing
                        requirements[item.Id.SubtypeName].Amount = ((amount / (decimal)bp.Results[0].Amount) * (decimal)item.Amount) + requirements[item.Id.SubtypeName].Amount;
                    }
                    else
                    {
                        // add new
                        requirements.Add(item.Id.SubtypeName, new BlueprintRequirement
                        {
                            Amount = (amount / (decimal)bp.Results[0].Amount) * (decimal)item.Amount,
                            TypeId = item.Id.TypeId.ToString(),
                            SubtypeId = item.Id.SubtypeName,
                            Id = item.Id
                        });
                    }

                    double timeMassMultiplyer = 1;
                    if (typeId == typeof(MyObjectBuilder_Ore) || typeId == typeof(MyObjectBuilder_Ingot))
                        timeMassMultiplyer = (double)bp.Results[0].Amount;

                    var ts = TimeSpan.FromSeconds(bp.BaseProductionTimeInSeconds * (double)amount / timeMassMultiplyer);
                    time += ts;
                }
            }

            timeTaken = time;
        }

        public static MyBlueprintDefinitionBase GetBlueprint(MyObjectBuilderType resultTypeId, string resultSubTypeId)
        {
            var bpList = SpaceEngineersCore.Resources.BlueprintDefinitions.Where(b => b.Results != null && b.Results.Any(r => r.Id.TypeId == resultTypeId && r.Id.SubtypeName == resultSubTypeId));
            return bpList.FirstOrDefault();
        }

        #endregion

        #region GetCubeDefinition

        public static MyCubeBlockDefinition GetCubeDefinition(MyObjectBuilderType typeId, MyCubeSize cubeSize, string subtypeName)
        {
            if (string.IsNullOrEmpty(subtypeName))
            {
                return SpaceEngineersCore.Resources.CubeBlockDefinitions.FirstOrDefault(d => d.CubeSize == cubeSize && d.Id.TypeId == typeId);
            }

            return SpaceEngineersCore.Resources.CubeBlockDefinitions.FirstOrDefault(d => d.Id.SubtypeName == subtypeName || (d.Variants != null && d.Variants.Any(v => subtypeName == d.Id.SubtypeName + v.Color)));
            // Returns null if it doesn't find the required SubtypeId.
        }

        #endregion

        #region GetBoundingBox

        public static BoundingBoxD GetBoundingBox(MyObjectBuilder_CubeGrid entity)
        {
            var min = new Vector3D(int.MaxValue, int.MaxValue, int.MaxValue);
            var max = new Vector3D(int.MinValue, int.MinValue, int.MinValue);

            foreach (var block in entity.CubeBlocks)
            {
                min.X = Math.Min(min.X, block.Min.X);
                min.Y = Math.Min(min.Y, block.Min.Y);
                min.Z = Math.Min(min.Z, block.Min.Z);
                max.X = Math.Max(max.X, block.Min.X);       // TODO: resolve cubetype size.
                max.Y = Math.Max(max.Y, block.Min.Y);
                max.Z = Math.Max(max.Z, block.Min.Z);
            }

            // scale box to GridSize
            var size = max - min;
            var len = entity.GridSizeEnum.ToLength();
            size = new Vector3D(size.X * len, size.Y * len, size.Z * len);

            // translate box according to min/max, but reset origin.
            var bb = new BoundingBoxD(Vector3D.Zero, size);

            // TODO: translate for rotation.
            //bb. ????

            // translate position.
            bb.Translate(entity.PositionAndOrientation.Value.Position);


            return bb;
        }

        #endregion

        #region LoadLocalization

        public static void LoadLocalization()
        {
            var culture = System.Threading.Thread.CurrentThread.CurrentUICulture;
            var languageTag = culture.IetfLanguageTag;

            var contentPath = ToolboxUpdater.GetApplicationContentPath();
            var localizationPath = Path.Combine(contentPath, @"Data\Localization");

            var codes = languageTag.Split(new char[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            var maincode = codes.Length > 0 ? codes[0] : null;
            var subcode = codes.Length > 1 ? codes[1] : null;

            MyTexts.Clear();

            if (GlobalSettings.Default.UseCustomResource.HasValue && GlobalSettings.Default.UseCustomResource.Value)
            {
                // no longer required, as Chinese is now officially in game.
                //AddLanguage(MyLanguagesEnum.ChineseChina, "zh", null, "Chinese", 1f, true);
            }

            MyTexts.LoadTexts(localizationPath, maincode, subcode);

            if (GlobalSettings.Default.UseCustomResource.HasValue && GlobalSettings.Default.UseCustomResource.Value)
            {
                // Load alternate localization in instead using game refined resources, as they may not yet exist.
                ResourceManager customGameResourceManager = new ResourceManager("SEToolbox.Properties.MyTexts", Assembly.GetExecutingAssembly());
                ResourceSet customResourceSet = customGameResourceManager.GetResourceSet(culture, true, false);
                if (customResourceSet != null)
                {
                    // Reflection copy of MyTexts.PatchTexts(string resourceFile)
                    foreach (DictionaryEntry dictionaryEntry in customResourceSet)
                    {
                        string text = dictionaryEntry.Key as string;
                        string text2 = dictionaryEntry.Value as string;
                        if (text != null && text2 != null)
                        {
                            MyStringId orCompute = MyStringId.GetOrCompute(text);
                            Dictionary<MyStringId, string> m_strings = typeof(MyTexts).GetStaticField<Dictionary<MyStringId, string>>("m_strings");
                            Dictionary<MyStringId, StringBuilder> m_stringBuilders = typeof(MyTexts).GetStaticField<Dictionary<MyStringId, StringBuilder>>("m_stringBuilders");

                            m_strings[orCompute] = text2;
                            m_stringBuilders[orCompute] = new StringBuilder(text2);
                        }
                    }
                }
            }
        }

        #endregion

        #region GetResourceName

        public static string GetResourceName(string value)
        {
            if (value == null)
                return null;

            MyStringId stringId = MyStringId.GetOrCompute(value);
            return MyTexts.GetString(stringId);
        }

        // Reflection copy of MyTexts.AddLanguage
        private static void AddLanguage(MyLanguagesEnum id, string cultureName, string subcultureName = null, string displayName = null, float guiTextScale = 1f, bool isCommunityLocalized = true)
        {
            // Create an empty instance of LanguageDescription.
            ConstructorInfo constructorInfo = typeof(MyTexts.LanguageDescription).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                new Type[] { typeof(MyLanguagesEnum), typeof(string), typeof(string), typeof(string), typeof(float), typeof(bool) }, null);
            MyTexts.LanguageDescription languageDescription = (MyTexts.LanguageDescription)constructorInfo.Invoke(new object[] { id, displayName, cultureName, subcultureName, guiTextScale, isCommunityLocalized });

            Dictionary<int, MyTexts.LanguageDescription> m_languageIdToLanguage = typeof(MyTexts).GetStaticField<Dictionary<int, MyTexts.LanguageDescription>>("m_languageIdToLanguage");
            Dictionary<string, int> m_cultureToLanguageId = typeof(MyTexts).GetStaticField<Dictionary<string, int>>("m_cultureToLanguageId");

            if (!m_languageIdToLanguage.ContainsKey((int)id))
            {
                m_languageIdToLanguage.Add((int)id, languageDescription);
                m_cultureToLanguageId.Add(languageDescription.FullCultureName, (int)id);
            }
        }

        #endregion
    }
}