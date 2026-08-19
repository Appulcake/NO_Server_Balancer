using System.Globalization;
using NuclearOption.SavedMission;

namespace NO_SB.MissionRestrictions;

internal static class MissionIdentifier
{
    internal static string Get(Mission mission)
    {
        if (mission.LoadKey.HasValue)
        {
            var loadKey = mission.LoadKey.Value;
            
            if (loadKey.WorkshopId.HasValue)
            {
                var workshopId = loadKey.WorkshopId.Value.m_PublishedFileId;
                
                if (workshopId != 0UL) return workshopId.ToString(CultureInfo.InvariantCulture);
            }
        }
        
        return mission.Name?.Trim() ?? string.Empty;
    }
}