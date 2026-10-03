using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>仅供本地机制验证：重载所属场景，不修改共享配置或其他场景。</summary>
public sealed class DemoLoopReset : MonoBehaviour
{
    public Entity? Enemy;
    public bool IsResetting { get; private set; }
    public bool EnemyAttacksEnabled => Enemy != null && Enemy.Brain.AiSource != null && Enemy.Brain.AiSource.EnableMeleeAttack;

    public void ToggleEnemyAttack()
    {
        if (!DebugSystem.IsEnabled || IsResetting || Enemy == null || Enemy.Brain.AiSource == null) return;
        Enemy.Brain.AiSource.EnableMeleeAttack = !Enemy.Brain.AiSource.EnableMeleeAttack;
        if (!Enemy.Brain.AiSource.EnableMeleeAttack && Enemy.Brain.InputSource is AITreeInputSource)
        {
            Enemy.Brain.StateMachine.ClearState(EnumStateLayer.Action);
            Enemy.Commands.AttackQueued = false;
        }
    }

    public void RequestReset()
    {
        Scene scene = gameObject.scene;
        if (IsResetting || !scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path)) return;
        IsResetting = true;
        StartCoroutine(Reload(scene));
    }

    private IEnumerator Reload(Scene scene)
    {
        bool wasActive = SceneManager.GetActiveScene() == scene;
        GameObject[] roots = scene.GetRootGameObjects();
        foreach (GameObject root in roots)
            foreach (Entity entity in root.GetComponentsInChildren<Entity>(true)) entity.Brain.EndPossession();
        // 旧角色先停止输入／相机，避免新场景装配期间两套角色同时响应。
        foreach (GameObject root in roots)
            if (root != gameObject) root.SetActive(false);
        yield return SceneManager.LoadSceneAsync(scene.path, LoadSceneMode.Additive);
        if (wasActive)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene loaded = SceneManager.GetSceneAt(i);
                if (loaded.path == scene.path && loaded.handle != scene.handle)
                {
                    SceneManager.SetActiveScene(loaded);
                    break;
                }
            }
        }
        yield return SceneManager.UnloadSceneAsync(scene); // 本组件随旧场景清理。
    }
}
