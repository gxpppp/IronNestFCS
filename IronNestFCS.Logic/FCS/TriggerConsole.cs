using System.Collections;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace IronNestFCS.Logic.FCS;

public class TriggerConsole {
    private LookAtTarget? taskCheck;
    private LookAtTarget? bulletCheck;
    private LookAtTarget? rotationCheck;
    private LookAtTarget? elevationCheck;
    private LookAtTarget? readyFire;
    private LookAtTarget? armLeft;
    private LookAtTarget? armRight;
    private SliderEnergyMomentumSpinner? fire;

    public bool TryBind() {
        var console = GameObject.Find(".Review Console Parent").transform;
        var buttons = new List<LookAtTarget>();
        
        for (var i = 0; i < console.childCount; ++i) {
            var child = console.GetChild(i);
            if (child.name.StartsWith(".Check Switch")) {
                buttons.Add(child.GetComponentInChildren<LookAtTarget>());
            }
        }

        if (buttons.Count != 5) {
            MelonLogger.Error("Can't bind trigger console.");
        }
        taskCheck = buttons[0];
        bulletCheck = buttons[1];
        rotationCheck = buttons[2];
        elevationCheck = buttons[3];
        readyFire = buttons[4];
        armLeft = GameObject.Find(".ArmingLeverParent Left").GetComponentInChildren<LookAtTarget>();
        armRight = GameObject.Find(".ArmingLeverParent Right").GetComponentInChildren<LookAtTarget>();
        fire = GameObject.Find(".Trigger Core").transform.FindChild(".Generator Spinner")
            .GetComponentInChildren<SliderEnergyMomentumSpinner>();
        return true;
    }

    public void Fire() {
        fire.AddEnergy(255);
    }

    public IEnumerator Arm(LeftRight leftRight) {
        var arm = leftRight == LeftRight.Left ? armLeft : armRight;
        arm.OnClickDown();
        yield return new WaitForSeconds(0.2f);
        arm.OnClickUp();
        yield return new WaitForSeconds(1f);
    }
    
    public IEnumerator ConfirmTask() {
        yield return new WaitForSeconds(0.1f);
        taskCheck.OnClickDown();
        yield return new WaitForSeconds(0.1f);
        taskCheck.OnClickUp();
    }

    public IEnumerator ConfirmBullet() {
        yield return new WaitForSeconds(0.1f);
        bulletCheck.OnClickDown();
        yield return new WaitForSeconds(0.1f);
        bulletCheck.OnClickUp();
    }

    public IEnumerator ConfirmRotation() {
        yield return new WaitForSeconds(0.1f);
        rotationCheck.OnClickDown();
        yield return new WaitForSeconds(0.1f);
        rotationCheck.OnClickUp();
    }

    public IEnumerator ConfirmElevation() {
        yield return new WaitForSeconds(0.1f);
        elevationCheck.OnClickDown();
        yield return new WaitForSeconds(0.1f);
        elevationCheck.OnClickUp();
    }

    public IEnumerator ReadyToFire() {
        yield return new WaitForSeconds(0.1f);
        readyFire.OnClickDown();
        yield return new WaitForSeconds(0.1f);
        readyFire.OnClickUp();
    }
}