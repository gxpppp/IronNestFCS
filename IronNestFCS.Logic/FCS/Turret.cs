using System.Collections;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace IronNestFCS.Logic.FCS;

public class Turret {
    private TurretController? _turret;


    public bool TryBind() {
        var turretObj = GameObject.Find("TurretSystem");
        if (turretObj == null) {
            MelonLogger.Error("[FCS] Aiming: Can't find TurretSystem");
            return false;
        }
        _turret = turretObj.GetComponent<TurretController>();
        return true;
    }
    
    public IEnumerator SetRotation(float angle) {
        if (_turret == null) {
            MelonLogger.Error("[FCS] Aiming: unbound TurretController");
            yield break;
        }

        _turret.DesiredRotation = -angle;
        yield return new WaitForSeconds(1f);
        float waited = 0f;
        while (_turret.rotationVelocity != 0 && waited < 30f) {
            yield return new WaitForSeconds(1f);
            waited += 1f;
        }
    }
    
}