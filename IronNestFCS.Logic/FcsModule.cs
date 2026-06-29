using Il2Cpp;
using IronNestFCS.Abstractions;
using IronNestFCS.Logic.FCS;
using UnityEngine.InputSystem;

namespace IronNestFCS.Logic;

/// <summary>
/// Logic 程序集的入口，由 Host 反射实例化（类型全名见 Host 的 LogicTypeName）。
/// 负责组装领域逻辑 <see cref="FSC"/>、点击检测 <see cref="ClickRaycaster"/> 与 UI <see cref="FcsWindow"/>，
/// 并把 Host 的生命周期回调转发下去。本身不含具体火控逻辑或绘制代码。
/// </summary>
public class FcsModule : IFcsModule
{
    private readonly FSC fcs = new();
    private FcsWindow? window;
    private TacticalRadar? radar;

    private bool autoSweep;
    private readonly HashSet<EntityLocation> swept = new(new EntityLocationComparer());

    public bool Initialize()
    {
        window = new FcsWindow(fcs);
        radar = new TacticalRadar(fcs);
        bool bound = fcs.TryBind();
        return bound;
    }

    public void Update()
    {
        fcs.Update();
        radar?.Update();

        if (autoSweep && radar != null && fcs.IsBound)
        {
            var alive = radar.AliveUnits;
            foreach (var unit in alive)
            {
                if (unit.Location != null && swept.Add(unit.Location))
                {
                    fcs.FireAtWorldPos(swept.Count, unit.WorldPos);
                }
            }
        }

        var kb = Keyboard.current;
        if (kb == null || !fcs.IsBound)
            return;

        if (kb.numpad0Key.wasPressedThisFrame)
        {
            autoSweep = !autoSweep;
            if (autoSweep) SweepAllHostiles();
            return;
        }
        if (kb.numpad1Key.wasPressedThisFrame) fcs.FireTarget(1);
        else if (kb.numpad2Key.wasPressedThisFrame) fcs.FireTarget(2);
        else if (kb.numpad3Key.wasPressedThisFrame) fcs.FireTarget(3);
        else if (kb.numpad4Key.wasPressedThisFrame) fcs.FireTarget(4);
    }

    private void SweepAllHostiles()
    {
        var alive = radar?.AliveUnits;
        if (alive == null || alive.Count == 0) return;
        for (int i = 0; i < alive.Count; i++)
        {
            if (alive[i].Location != null)
                swept.Add(alive[i].Location);
            fcs.FireAtWorldPos(i + 1, alive[i].WorldPos);
        }
}

/// <summary>
/// IL2CPP 对象的引用相等比较器，确保同一个 EntityLocation 不重复入队。
/// </summary>
internal sealed class EntityLocationComparer : IEqualityComparer<EntityLocation>
{
    public bool Equals(EntityLocation? x, EntityLocation? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        return x.Pointer == y.Pointer;
    }

    public int GetHashCode(EntityLocation obj)
    {
        return obj.Pointer.GetHashCode();
    }
}

    public void OnGui()
    {
        window?.OnGui();
        radar?.OnGui();
    }

    public void Shutdown()
    {
        fcs.Dispose();
        window = null;
        radar = null;
    }
}
