#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;


namespace Seeder
{
    // GameDl과 관련된 json 데이터 처리.
    public sealed partial class GameDL
    {
        // json데이터를 parsing하여 Dl로 구성.
        public static bool Parse(string jsonText, ref Dictionary<string, Dictionary<string, GameDL>> dlAll)
        {
            var rootJo = Json.Deserialize(jsonText) as Dictionary<string, object>;
            if (rootJo == null)
            {
                Debug.LogError("GameDL.ParseFromJson() => root json is null!");
                return false;
            }
            if (!rootJo.ContainsKey(DLKeyword.table_setting))
            {
                Debug.LogError("GameDL.ParseFromJson() => table setting is NOT Exist!");
                return false;
            }

            var tableInfoSet = rootJo[DLKeyword.table_setting] as Dictionary<string, object>;
            // 테이블 구조타입 및 병합 정보 체크.                        
            foreach (var tableInfoRow in tableInfoSet)
            {
                var tableName = tableInfoRow.Key;
                var settingJo = tableInfoRow.Value as Dictionary<string, object>;

                // 저장할 dl groupSet 구성.
                var groupName = settingJo[AttrName.data_group] as string;
                if (!dlAll.TryGetValue(groupName, out var groupSet))
                {
                    groupSet = new Dictionary<string, GameDL>();
                    dlAll.Add(groupName, groupSet);
                }
                // 병합할 테이블의 row를 하나씩 parsing.                
                var targetTable = rootJo[tableName] as Dictionary<string, object>;
                foreach (var row in targetTable)
                {
                    var idKey = row.Key;
                    var valueJo = row.Value as Dictionary<string, object>;
                    var dl = Parse(rootJo, settingJo, valueJo, idKey);
                    if (dl != null)
                    {
                        groupSet.Add(idKey, dl);
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static GameDL Parse(Dictionary<string, object> rootJo
            , Dictionary<string, object> commonJo, Dictionary<string, object> privateJo, string idKey)
        { 
            // _rootDic에 들어갈 대략적인 count 개수 계산하여 capacity 지정.
            // 자동 확장 방지를 위해 여유분으로 4를 추가한다.
            var capacity = 4;
            capacity += commonJo.Count;
            foreach (var elem in privateJo)
            {
                var name = elem.Key;
                // join 접두사가 없으면 +1 한다.
                if (!name.StartsWith(DLKeyword.join_prefix))
                {
                    capacity++;
                    continue;
                }

                var tableName = (elem.Value as string).Split(NameUtil.Dot)[0];
                if (!DLTool.IsValidString(tableName))
                {
                    continue;
                }

                var joinTableJo = rootJo[tableName] as Dictionary<string, object>;
                capacity += joinTableJo.Count;
            }
            var dl = new GameDL();
            dl._storage = new Dictionary<(string, string), Dictionary<string, DataBlock>>(capacity);

            // 세부 데이터 parsing.
            foreach (var elem in privateJo)
            {
                var name = elem.Key;

                var groupSeparator = '.';
                // groupSeparator가 포함되어 있으면 groupName과 속성이름을 분리하여 철.
                if (name.Contains(groupSeparator))
                {
                    var nameArray = name.Split(groupSeparator);
                    // 첫번째 값은 GroupName, 두번째 값은 속성이름으로 사용. RowKey는 string.Empty이다.
                    var groupRowKey = (nameArray[0], string.Empty);
                    AddData(dl, groupRowKey, nameArray[1], elem.Value);
                }
                // join 접두사가 있는것은 해당값이 가리키는 table값을 parsing하여 가져온다.
                else if (name.StartsWith(DLKeyword.join_prefix))
                {
                    // join 테이블 및 key 이름 가져오기.
                    var joinValueArray = (elem.Value as string).Split(NameUtil.Dot);
                    var joinTableName = joinValueArray[0];
                    if (!DLTool.IsValidString(joinTableName))
                    {
                        continue;
                    }

                    // 처음(join)과 두번째(join의 type이름), 마지막(index)을 제외한 가운데 있는 이름이 categoryName이 된다.
                    var startIndex = name.IndexOf(NameUtil.SeparatorDefaultChar) + 1; // join_ 다음의 index;
                    startIndex = name.IndexOf(NameUtil.SeparatorDefaultChar, startIndex) + 1; // join의type_ 다음의 index;
                    var length = name.LastIndexOf(NameUtil.SeparatorDefaultChar) - startIndex;
                    // length가 1보다 작다면 LastIndexOf를 사용하지 말고 name의 마지막 index의 length를 사용한다.
                    if (length <= 0)
                    {
                        length = name.Length - startIndex;
                    }
                    var categoryName = name.Substring(startIndex, length);

                    var joinJo = rootJo[joinTableName] as Dictionary<string, object>;
                    // 테이블을 전체를 join할 경우. 모든 값을 추가.
                    if (name.StartsWith(DLKeyword.join_all_prefix))
                    {
                        // join할 테이블 전체 parsing.
                        foreach (var jElem in joinJo)
                        {
                            // 데이터를 parsing 하여 category와 key를 조합해서 등록.
                            var newDataSet = DLTool.Parse(jElem.Value as Dictionary<string, object>);
                            var searchKey = (categoryName, jElem.Key);
                            if (!dl._storage.TryGetValue(searchKey, out var dataDic))
                            {
                                dataDic = new Dictionary<string, DataBlock>(newDataSet.Count);
                                dl._storage.Add(searchKey, dataDic);
                            }
                            dataDic.AddRange(newDataSet);
                        }
                    }
                    // 특정 행(row)의 데이터만 join할 경우. 해당값만 가져와서 추가.
                    else if (name.StartsWith(DLKeyword.join_row_prefix))
                    {                        
                        var joinRowKey = joinValueArray.Length > 1 ? joinValueArray[1] : string.Empty;
                        // 입력된 joinKey가 없다면 idKey를 입력값으로 한다.
                        var rowKey = joinRowKey.IsNullOrEmpty() ? idKey : joinRowKey;
                        if (rowKey.IsNullOrEmpty() || !joinJo.TryGetValue(rowKey, out var rowValue))
                        {
                            Debug.LogError($"join의 rowKey에 해당하는 값이 없습니다. joinTable:{joinTableName}, rowkey:{rowKey}");
                            return null;
                        }

                        // rowkey에 해당하는 값을 parsing.
                        var newDataSet = DLTool.Parse(rowValue as Dictionary<string, object>);
                        // 어차피 하나의 행만 join하므로 searchKey의 Row는 검색편의성을 위해 빈문자열로 한다.
                        var searchKey = (categoryName, string.Empty);
                        if (!dl._storage.TryGetValue(searchKey, out var dataDic))
                        {
                            dataDic = new Dictionary<string, DataBlock>(newDataSet.Count);
                            dl._storage.Add(searchKey, dataDic);
                        }
                        dataDic.AddRange(newDataSet);
                    }
                    // 조건에 맞는 join이 없는 경우.
                    else
                    {
                        Debug.LogError($"join keyword 오류입니다. key:{name}");
                        return null;
                    }
                }
                // parsing이 필요한 특수 포맷이 아닌 경우는 기본 key값으로 데이터를 추가한다.
                else
                {
                    AddDataToDefault(dl, name, elem.Value);
                }
            }

            // 공유값 parsing 하여 추가.
            foreach (var elem in commonJo)
            {
                AddDataToDefault(dl, elem.Key, elem.Value, false);
            }

            return dl;
        }

        // 데이터 추가.
        private static void AddData(GameDL dl, (string, string) groupRowKey, string name, object value, bool force = true)
        {
            if (!DLTool.CanParse(name))
            {
                return;
            }
            
            // 기존에 값이 없으면 공유값에 설정된 값을 추가한다.
            if (!dl._storage.TryGetValue(groupRowKey, out var dataDic))
            {
                dataDic = new Dictionary<string, DataBlock>();
                dl._storage.Add(groupRowKey, dataDic);
            }

            if (force || !dataDic.ContainsKey(name))
            {
                var dataBlock = DLTool.Parse(name, value);
                if (dataBlock != null)
                {
                    dataDic.Add(name, dataBlock);
                }
            }
        }
        // 기본key(string.Empty)로 데이터 추가.
        private static void AddDataToDefault(GameDL dl, string name, object value, bool force = true)
        {
            AddData(dl, (DLKeyword.group_default, string.Empty), name, value, force);
        }
    }
}
#endif
