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
    private OdometerDisplay? remainingCharges;

    private TextMeshPro shellId;

    public bool TryBind(string surfix) {
        this._surfix = surfix;
        
        var gunSystem = GameObject.Find("Gun System " + surfix).transform;
        var reloadingConsole = gunSystem.Find("--Reloading Console");
        if (reloadingConsole == null) {
            MelonLogger.Error($"[FCS] GunSystem {surfix}: Can't find --Reloading Console");
            return false;
        }

        remainingCharges = reloadingConsole.GetComponentInChildren<OdometerDisplay>();
        
        nextBulletButton = 
            reloadingConsole.Find("Universal Button Move Cylinder")
                .GetComponent<LookAtTarget>();    
        shellSelector = gunSystem.GetComponentInChildren<CylinderShellSelector>();
        
        shellId = GameObject.Find("Shell ID " + surfix)
            .GetComponent<TextMeshPro>();
        var loadShell = reloadingConsole.FindChild("Universal Button Load shell Rammer");
        if (loadShell == null) {
            MelonLogger.Error($"[FCS] GunSystem {surfix}: Can't find Universal Button Load shell Rammer");
            return false;
        }
        loadBulletButton = loadShell.GetComponent<LookAtTarget>();

        var powderController = reloadingConsole.Find("PowderChargeController");
        for (var i = 0; i < powderController.childCount; ++i) {
            var child = powderController.GetChild(i);
            if (!child.name.StartsWith("Button Dispencer")) continue;
            var button = child.GetComponent<LookAtTarget>();
            if (button == null) {
                MelonLogger.Error($"[FCS] GunSystem {surfix}: Found {child.name} but lack of LookAtTarget Component");
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
        return gunController != null && gunController.CanFire;
    }

    public IEnumerator SetElevation(float elevation) {
        if (elevationLever == null) { yield break; }
        elevationLever.SetSliderValue(elevation);
        yield return new WaitForSeconds(0.1f);
        float waited = 0f;
        while (gunController != null && Mathf.Abs(gunController.ElevationErrorDeg) > 0.1f && waited < 30f) {
            elevationLever.SetSliderValue(elevation);
            yield return new WaitForSeconds(1f);
            waited += 1f;
        }
    }
    
    public string? BulletInChamber() {
        return gunController?.ChamberedShellBlueprint?.shellDefinition?.ShellId?.ToUpper();
    }
    
    public bool IsChamberEmpty() {
        return BulletInChamber() == null;
    }

    private void RefreshBullets() {
        bullets.Clear();
        if (shellSelector == null) return;
        foreach (var shell in shellSelector.bullets) {
            bullets.Add(shell?.GetComponent<ShellBlueprint>()?.shellDefinition?.ShellId?.ToUpper());
        }
    }

    public void NextBullet() {
        if (nextBulletButton == null) return;
        nextBulletButton.OnClickDown();
    }
    
    public IEnumerator LoadBullet(BulletType type) {
        RefreshBullets();
        var typeStr = type.ToString().ToUpper();
        var index = bullets.IndexOf(typeStr);
        if (index == -1) {
            MelonLogger.Error($"[FCS] GunSystem {_surfix}: " +
                              $"No {type} available in cylinder, current bullets: {string.Join(", ", bullets)}");
            yield break;
        }
        
        for (var i = 0; i < bullets.Count; ++i) {
            if (bullets[0] == typeStr) {
                break;
            };
            NextBullet();
            yield return new WaitForSeconds(1.5f);
            RefreshBullets();
        }
        if (bullets[0] != typeStr) {
            MelonLogger.Error($"[FCS] GunSystem {_surfix}: Can't find {type} after rotation, " +
                              $"current: {string.Join(", ", bullets)}");
            yield break;
        }
        yield return FcsSceneInteractor.WaitAndClick(loadBulletButton!);
        if (gunController != null)
            yield return GameStateWatcher.WaitForReloadComplete(gunController);
    }

    private IEnumerator SelectPowder(int count) {
        for (var i = 0; i < count; i++) {
            if (i >= powderButtons.Count || powderButtons[i] == null || powderButtons[i].gameObject == null) {
                RefreshPowderButtons();
                if (i >= powderButtons.Count || powderButtons[i] == null) {
                    MelonLogger.Error($"[GunSystem] SelectPowder: button {i} invalid after refresh");
                    yield break;
                }
            }
            yield return FcsSceneInteractor.WaitAndClick(powderButtons[i]);
        }
    }

    private void RefreshPowderButtons() {
        powderButtons.Clear();
        var gunSystem = GameObject.Find("Gun System " + _surfix)?.transform;
        var reloadingConsole = gunSystem?.Find("--Reloading Console");
        var powderController = reloadingConsole?.Find("PowderChargeController");
        if (powderController == null) return;
        for (var i = 0; i < powderController.childCount; ++i) {
            var child = powderController.GetChild(i);
            if (!child.name.StartsWith("Button Dispencer")) continue;
            var button = child.GetComponent<LookAtTarget>();
            if (button != null) powderButtons.Add(button);
        }
    }

    public IEnumerator LoadPowder(int count) {
        if (loadPowderButton == null || loadPowderButton.gameObject == null) {
            var gunSystem = GameObject.Find("Gun System " + _surfix)?.transform;
            var reloadingConsole = gunSystem?.Find("--Reloading Console");
            loadPowderButton = reloadingConsole?.FindChild("Universal Button Charge Rammer (1)")
                ?.GetComponent<LookAtTarget>();
            if (loadPowderButton == null) {
                MelonLogger.Error($"[GunSystem] LoadPowder: rammer button missing");
                yield break;
            }
        }
        yield return SelectPowder(count);
        yield return FcsSceneInteractor.WaitAndClick(loadPowderButton);
        if (gunController != null)
            yield return GameStateWatcher.WaitForReloadComplete(gunController);
    }

    /// <summary>直接退弹（不发射），失败则回退到旧 dump 方式</summary>
    public IEnumerator EjectChamberedShell()
    {
        var gunSystem = GameObject.Find("Gun System " + _surfix)?.transform;
        var rc = gunSystem?.GetComponentInChildren<ArtilleryReloadController>();
        if (rc != null)
        {
            rc.EjectChamberedShell();
            yield return new WaitForSeconds(1f);
            if (gunController != null)
                yield return GameStateWatcher.WaitForReloadComplete(gunController);
        }
        else
        {
            // fallback: 装 1 包药 → 平射清膛
            MelonLogger.Msg($"[GunSystem] Eject not available, fallback to dump fire");
            yield return LoadPowder(1);
            while (!CanFire() && gunController != null) yield return new WaitForSeconds(1f);
        }
    }

    /// <summary>直接击发（用 GunController.RequestFire 代替 spinner）</summary>
    public void RequestFire()
    {
        if (gunController != null)
            gunController.RequestFire();
    }

    public bool HaveBulletInCylinder(BulletType type) {
        RefreshBullets();
        return bullets.Contains(type.ToString().ToUpper());
    }
    
    public bool HaveEmptyShellInCylinder() {
        RefreshBullets();
        return bullets.Contains(null);
    }

    public IEnumerator WaitBackToIdle() {
        float waited = 0f;
        while (gunController != null && Mathf.Abs(gunController.CurrentElevationSpeed) > 0.01f && waited < 30f) {
            yield return new WaitForSeconds(0.1f);
            waited += 0.1f;
        }
        if (gunController != null) {
            yield return GameStateWatcher.WaitForReloadComplete(gunController, 20f);
            if (!gunController.ExternalReloadLoweringLocked) {
                gunController.SetExternalReloadLoweringLocked(true);
                yield return new WaitForSeconds(0.5f);
            }
        }
    }

    public IEnumerator WaitFire() {
        float waited = 0f;
        while (gunController != null && !gunController.IsReloading && waited < 30f) {
            yield return new WaitForSeconds(0.1f);
            waited += 0.1f;
        }
    }
    
    public int RemainingCharges() {
        return remainingCharges != null ? (int)remainingCharges.CurrentNumber : 0;
    }

    /// <summary>炮管当前状态快照</summary>
    public struct GunState {
        public string? ChamberedShell;
        public bool CanFire;
        public bool PendingReload;
        public float ElevationVelocity;
        public float CurrentElevation;
        public int ChargesRemaining;
        public string[] CylinderBullets;
    }

    public GunState GetState() {
        RefreshBullets();
        return new GunState {
            ChamberedShell = BulletInChamber(),
            CanFire = CanFire(),
            PendingReload = gunController != null && gunController.IsReloading,
            ElevationVelocity = gunController != null ? gunController.CurrentElevationSpeed : 0f,
            CurrentElevation = gunController != null ? gunController.CurrentElevation : 0f,
            ChargesRemaining = RemainingCharges(),
            CylinderBullets = bullets.Where(b => b != null).ToArray()!
        };
    }

}
