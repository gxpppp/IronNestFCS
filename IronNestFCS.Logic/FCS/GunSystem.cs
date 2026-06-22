using System.Collections;
using Il2Cpp;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;

namespace IronNestFCS.Logic.FCS;


public enum BulletType {
    AP = 1,
    HCHE = 2,
    HE = 3,
    STAR = 4,
    SMK = 5,
}

public class GunSystem {
    private string _surfix = "";

    private CylinderShellSelector? shellSelector;
    
    private List<string?> bullets = new();
    private LookAtTarget? nextBulletButton;
    private LookAtTarget? loadBulletButton;
    private List<LookAtTarget> powderButtons = new();
    private LookAtTarget? loadPowderButton;
    private GunController? gunController;
    private LinearSliderInteractable? elevationLever;

    private TextMeshPro shellId;

    public bool TryBind(string surfix) {
        this._surfix = surfix;
        
        var gunSystem = GameObject.Find("Gun System " + surfix).transform;
        var reloadingConsole = gunSystem.Find("--Reloading Console");
        if (reloadingConsole == null) {
            MelonLogger.Error($"[FCS] GunSystem {surfix}: 找不到 --Reloading Console");
            return false;
        }
        
        nextBulletButton = 
            reloadingConsole.Find("Universal Button Move Cylinder")
                .GetComponent<LookAtTarget>();    
        shellSelector = gunSystem.GetComponentInChildren<CylinderShellSelector>();
        
        shellId = GameObject.Find("Shell ID " + surfix)
            .GetComponent<TextMeshPro>();
        var loadShell = reloadingConsole.FindChild("Universal Button Load shell Rammer");
        if (loadShell == null) {
            MelonLogger.Error($"[FCS] GunSystem {surfix}: 找不到 Universal Button Load shell Rammer");
            return false;
        }
        loadBulletButton = loadShell.GetComponent<LookAtTarget>();

        var powderController = reloadingConsole.Find("PowderChargeController");
        for (var i = 0; i < powderController.childCount; ++i) {
            var child = powderController.GetChild(i);
            if (!child.name.StartsWith("Button Dispencer")) continue;
            var button = child.GetComponent<LookAtTarget>();
            if (button == null) {
                MelonLogger.Error($"[FCS] GunSystem {surfix}: 找到 {child.name} 却没有 LookAtTarget 组件");
                return false;
            }
            powderButtons.Add(button);
        }

        loadPowderButton = reloadingConsole.FindChild("Universal Button Charge Rammer (1)").GetComponent<LookAtTarget>();
        gunController = GameObject.Find("Gun"+surfix).GetComponent<GunController>();
        elevationLever = GameObject.Find(".Elevation Lever Baseplate")?.transform.FindChild(".Elevation Lever " + surfix)
            .GetComponent<LinearSliderInteractable>();
        return true;
    }
    
    public bool CanFire() {
        return gunController.CanFire;
    }

    public IEnumerator SetElevation(float elevation) {
        elevationLever.SetSliderValue(elevation);
        yield return new WaitForSeconds(0.1f);
        while (gunController.CurrentElevation != elevation) {
            elevationLever.SetSliderValue(elevation);
            yield return new WaitForSeconds(1f);
        }
    }
    
    public string? BulletInChamber() {
        return gunController?.ChamberedShellBlueprint?.shellDefinition?.ShellId;
    }
    
    public bool IsChamberEmpty() {
        return BulletInChamber() == null;
    }

    private void RefreshBullets() {
        bullets.Clear();
        if (shellSelector == null) return;
        foreach (var shell in shellSelector.bullets) {
            bullets.Add(shell?.GetComponent<ShellBlueprint>()?.shellDefinition?.ShellId);
        }
    }

    public void NextBullet() {
        if (nextBulletButton == null) {
            MelonLogger.Error($"[FCS] GunSystem {_surfix}: NextBulletButton 未绑定");
        }
        MelonLogger.Msg("[GunSystem] NextBullet");
        nextBulletButton!.OnClickDown();
    }
    
    /// <summary>
    /// 装填指定弹种：先把弹仓转到目标弹，再按装填。转弹仓每步之间要等 1 秒
    /// （游戏有转动动画/物理）。返回 IEnumerator，调用方用 yield return 等待它跑完。
    /// 必须走协程而非 async：continuation 要留在主线程才能安全访问 IL2CPP 对象。
    /// </summary>
    public IEnumerator LoadBullet(BulletType type) {
        RefreshBullets();
        var index = bullets.IndexOf(type.ToString());
        if (index == -1) {
            MelonLogger.Error($"[FCS] GunSystem {_surfix}: 没有 {type} 可以装填");
            yield break;
        }
        for (var i = 0; i < index; i++) {
            NextBullet();
            yield return new WaitForSeconds(2f);
        }
        yield return FcsSceneInteractor.WaitAndClick(loadBulletButton!);
    }

    private IEnumerator SelectPowder(int count) {
        for (var i = 0; i < count; i++) {
            yield return FcsSceneInteractor.WaitAndClick(powderButtons[i]!);
        }
    }

    public IEnumerator LoadPowder(int count) {
        yield return SelectPowder(count);
        yield return FcsSceneInteractor.WaitAndClick(loadPowderButton!);
    }

    public bool HaveBulletInCylinder(BulletType type) {
        RefreshBullets();
        return bullets.Contains(type.ToString());
    }
    
    public bool HaveEmptyShellInCylinder() {
        RefreshBullets();
        return bullets.Contains(null);
    }

    public IEnumerator WaitBackToIdle() {
        while (gunController.elevationChangeVelocity != 0) {
            yield return new WaitForSeconds(0.1f);
        }
        yield return new WaitForSeconds(13);
    }

    public IEnumerator WaitFire() {
        while (!gunController.pendingReload) {
            yield return new WaitForSeconds(0.1f);
        }
    }

}