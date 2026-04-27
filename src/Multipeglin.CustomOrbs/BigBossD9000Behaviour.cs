using System;
using UnityEngine;

namespace Multipeglin.CustomOrbs;

/// <summary>
/// Per-bounce 1-in-N roll. If any bounce in the shot rolls a hit, the orb
/// deals 9000/9000 instead of its base 0/0. Chance is per-level.
/// </summary>
public class BigBossD9000Behaviour : MonoBehaviour
{
    public int OneInChance = 100;

    private PachinkoBall _ball;

    public bool CritRolledThisShot { get; private set; }

    private void Awake()
    {
        _ball = GetComponent<PachinkoBall>();
    }

    private void OnEnable()
    {
        CritRolledThisShot = false;
        if (_ball == null)
        {
            return;
        }

        _ball.OnPachinkoBallPegHit = (PachinkoBall.PachinkoBallPegHit)Delegate.Combine(
            _ball.OnPachinkoBallPegHit, new PachinkoBall.PachinkoBallPegHit(OnPegHit));
    }

    private void OnDisable()
    {
        if (_ball == null)
        {
            return;
        }

        _ball.OnPachinkoBallPegHit = (PachinkoBall.PachinkoBallPegHit)Delegate.Remove(
            _ball.OnPachinkoBallPegHit, new PachinkoBall.PachinkoBallPegHit(OnPegHit));
    }

    private void OnPegHit(Peg peg, PachinkoBall ball)
    {
        if (CritRolledThisShot)
        {
            return;
        }

        if (OneInChance <= 1 || UnityEngine.Random.Range(0, OneInChance) == 0)
        {
            CritRolledThisShot = true;
        }
    }
}
