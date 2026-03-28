using Battle.Attacks;
using UnityEngine;

namespace PeglinMods.Multiplayer.Utility;

public class OrbIdentifier
{
    public string GetId(GameObject ball)
    {
        if (ball == null)
            return "unknown";
        var attack = ball.GetComponent<Attack>();
        if (attack != null && !string.IsNullOrEmpty(attack.locNameString))
            return attack.locNameString;
        return ball.name;
    }

    public int GetLevel(GameObject ball)
    {
        if (ball == null)
            return 0;
        var attack = ball.GetComponent<Attack>();
        if (attack != null)
            return attack.Level;
        return 0;
    }

    public GameObject Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        var attacks = Object.FindObjectsOfType<Attack>();
        foreach (var attack in attacks)
        {
            if (attack.locNameString == id)
                return attack.gameObject;
        }

        return GameObject.Find(id);
    }
}
