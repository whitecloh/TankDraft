using System;
using System.IO;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
namespace TankDraft.Infrastructure
{
    [MovedFrom(true, "TankDraft.Bootstrap", "TankDraft.Bootstrap", null)]
    [CreateAssetMenu(menuName = "TankDraft/Bootstrap/Local profile settings")]
    public sealed class LocalProfileSettings : ScriptableObject
    {
        [SerializeField] private string _fileName = "local-profile-v1.json";
        public string GetPath()
        {
            if(string.IsNullOrWhiteSpace(_fileName) || Path.GetFileName(_fileName)!=_fileName || _fileName.IndexOfAny(Path.GetInvalidFileNameChars())>=0)
                throw new InvalidOperationException("Profile file name must be a simple file name.");
            return Path.Combine(UnityEngine.Application.persistentDataPath,"TankDraft",_fileName);
        }
    }
}
