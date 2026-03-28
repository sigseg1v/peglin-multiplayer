using System.Collections.Generic;
using Battle.Enemies;
using UnityEngine;

namespace PeglinMods.Multiplayer.Utility;

public class EnemyIdentifier
{
    private readonly Dictionary<string, Enemy> _idToEnemy = new Dictionary<string, Enemy>();
    private readonly Dictionary<Enemy, string> _enemyToId = new Dictionary<Enemy, string>();
    private int _nextSlot;

    public string GetId(Enemy enemy)
    {
        if (enemy == null) return "unknown";

        if (_enemyToId.TryGetValue(enemy, out var existing))
            return existing;

        var id = enemy.locKey + "_" + _nextSlot++;
        _idToEnemy[id] = enemy;
        _enemyToId[enemy] = id;
        return id;
    }

    public Enemy Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        if (_idToEnemy.TryGetValue(id, out var enemy) && enemy != null)
            return enemy;

        // Fallback: scan active enemies
        var enemies = Object.FindObjectsOfType<Enemy>();
        foreach (var e in enemies)
        {
            if (GetId(e) == id)
                return e;
        }

        return null;
    }

    public void Clear()
    {
        _idToEnemy.Clear();
        _enemyToId.Clear();
        _nextSlot = 0;
    }
}
