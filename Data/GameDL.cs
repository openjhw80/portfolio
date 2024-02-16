using System.Collections.Generic;


namespace Seeder
{
    // game 시스템을 구성하는 각종 데이터를 저장하는 클래스(GameDataLibrary)
    public sealed partial class GameDL : IDataLibrary
    {
        // 모든 main 및 sub 데이터 dictionary를 담고 있는 root dictionary.                
        private Dictionary<(string, string), Dictionary<string, DataBlock>> _storage;        

        //================================================================================
        // 기초 메서드(생성자, 초기화등) 모음
        //================================================================================          

        //================================================================================
        // interface 메서드 모음
        //================================================================================              
        //========= IDataLibrary 인터페이스 구현 ==========//
        public bool Find(string name, out DataBlock value)
        {
            return Find(new DLSearchKey() { Name = name }, out value);
        }

        public bool Find(DLSearchKey searchKey, out DataBlock value)
        {
            value = null;
            if (_storage.TryGetValue((searchKey.Group, searchKey.Row), out var dataSet))
            {
                if (dataSet.TryGetValue(searchKey.Name, out value))
                {
                    return true;
                }
            }

            // searchKey.Key 값 위치에서 찾을 때, 해당 값이 없으면 공통으로 사용하는 곳(key가 empty)에서 다시 한 번 찾는다.            
            if (!string.IsNullOrEmpty(searchKey.Row)
            && _storage.TryGetValue((searchKey.Group, string.Empty), out var commonDataSet))
            {
                if (commonDataSet.TryGetValue(searchKey.Name, out value))
                {
                    return true;
                }
            }

            return false;
        }
        public bool Exist(string name)
        {
            return Find(new DLSearchKey() { Name = name }, out DataBlock _);
        }
        public bool Exist(DLSearchKey searchKey)
        {
            return Find(searchKey, out DataBlock _);
        }
        public bool ExistRow(DLSearchKey searchKey)
        {
            return _storage.ContainsKey((searchKey.Group, searchKey.Row));
        }
        public bool ExistGroup(string groupName)
        {
            if (groupName.IsNullOrEmpty())
            {
                return false;
            }

            foreach (var elem in _storage)
            {
                if (elem.Key.Item1 == groupName)
                {
                    return true;
                }
            }

            return false;
        }

        //================================================================================
        // 일반 메서드 모음 
        //================================================================================
        public Dictionary<(string, string), Dictionary<string, DataBlock>>.Enumerator GetEnumerator()
        {
            return _storage.GetEnumerator();
        }
        public Dictionary<(string, string), Dictionary<string, DataBlock>>.KeyCollection GetAllKeys()
        {
            return _storage.Keys;
        }
        public Dictionary<string, DataBlock>.KeyCollection GetMainKeys()
        {
            return GetKeys(new DLSearchKey());
        }
        public Dictionary<string, DataBlock>.KeyCollection GetKeys(DLSearchKey searchKey)
        {
            if (_storage.TryGetValue((searchKey.Group, searchKey.Row), out var dic))
            {
                return dic.Keys;
            }
            else
            {
                return null;
            }
        }  

        public int GetRowCount(string groupName)
        {
            if (groupName.IsNullOrEmpty())
            {
                return 0;
            }

            var count = 0;
            foreach (var elem in _storage)
            {
                if (elem.Key.Item1 == groupName)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
