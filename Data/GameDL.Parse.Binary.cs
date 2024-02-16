using System.IO;
using System.Collections.Generic;
using UnityEngine;


namespace Seeder
{
    // GameDl과 관련된 binary 데이터 처리.
    public sealed partial class GameDL
    {
        public const string CryptoKey = "crypto32crypto32crypto32crypto32";

        // json데이터를 parsing하여 Dl로 구성.
        public static void Parse(byte[] data, string fileName, ref Dictionary<string, Dictionary<string, GameDL>> dlAll)
        {
            var crypto = CryptoXOR.Create(CryptoKey, HashUtil.ComputeHashCode(fileName));
            data = crypto.Decrypt(data);
            var stream = new MemoryStream(data);
            var reader = new BinaryReader(stream);
            
            var groupCount = reader.ReadInt32();
            for(var i = 0; i < groupCount; i++)
            {
                var groupName = reader.ReadString();
                groupName = string.Intern(groupName);
                if (!dlAll.TryGetValue(groupName, out var dlSet))
                {
                    dlSet = new Dictionary<string, GameDL>();
                    dlAll.Add(groupName, dlSet);
                }                

                var dlCount = reader.ReadInt32();
                for(var k = 0; k < dlCount; k++)
                {
                    // 각 dl별 parsing하여 저장.
                    var dlKey = reader.ReadString();
                    dlKey = string.Intern(dlKey);
                    dlSet.Add(dlKey, Parse(reader));
                }
            }
        }

        private static GameDL Parse(BinaryReader reader)
        {
            // 기본 데이터 parsing.
            var storageCount = reader.ReadInt32();
            var storage = new Dictionary<(string, string), Dictionary<string, DataBlock>>(storageCount);            
            for (var i = 0; i < storageCount; i++)
            {
                // searkey를 구성해서 dataSet 준비.
                var groupName = reader.ReadString();
                groupName = string.Intern(groupName);
                var rowName = reader.ReadString();
                rowName = string.Intern(rowName);
                var searchKey = (groupName, rowName);
                if (!storage.TryGetValue(searchKey, out var dataSet))
                {
                    dataSet = new Dictionary<string, DataBlock>();
                    storage.Add(searchKey, dataSet);
                }

                // 각 data를 읽어와서 DataBlock 구성.
                var dataCount = reader.ReadInt32();
                for (var k = 0; k < dataCount; k++)
                {
                    var dataKey = reader.ReadString();
                    dataKey = string.Intern(dataKey);
                    var dataBlock = DataBlock.FromBinary(reader);
                    if (dataBlock == null)
                    {
                        Debug.LogError("GameDL.Parse(BinaryReader) => DataBlock is null!!");
                        continue;
                    }

                    dataSet.Add(dataKey, dataBlock);
                }
            }

            var dl = new GameDL();
            dl._storage = storage;            
            return dl;
        }



#if UNITY_EDITOR
        public void WriteTo(BinaryWriter writer)
        {
            // 기본 데이터 저장소 쓰기.
            writer.Write(_storage.Count);
            foreach (var elem in _storage)
            {                
                writer.Write(elem.Key.Item1);
                writer.Write(elem.Key.Item2);
                WriteTo(writer, elem.Value);
            }
        }
        private void WriteTo(BinaryWriter writer, Dictionary<string, DataBlock> dbSet)
        {
            writer.Write(dbSet.Count);

            foreach (var elem in dbSet)
            {   
                writer.Write(elem.Key);
                elem.Value.WriteTo(writer);
            }
        }
#endif
    }
}
