using UnityEngine;
using UnityEditor;

public static class AnimationBindingDebug
{
    [MenuItem("Tools/Check Selected Animator Bindings")]
    private static void CheckBindings()
    {
        GameObject selected = Selection.activeGameObject;

        if (selected == null)
        {
            Debug.LogError("Select the robot in the Hierarchy.");
            return;
        }

        Animator animator = selected.GetComponent<Animator>();

        if (animator == null ||
            animator.runtimeAnimatorController == null)
        {
            Debug.LogError("Missing Animator or Controller.");
            return;
        }

        Debug.Log(
            $"Avatar valid: " +
            $"{(animator.avatar != null && animator.avatar.isValid)}"
        );

        Debug.Log(
            $"Animation-Clips of Animator: " +
            $"{animator.runtimeAnimatorController.animationClips.Length}"
        );
        
        foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);

            int missing = 0;

            foreach (var binding in bindings)
            {
                if (string.IsNullOrEmpty(binding.path))
                    continue;

                if (animator.transform.Find(binding.path) == null)
                {
                    missing++;

                    Debug.LogWarning(
                        $"Missing path: {binding.path}"
                    );
                }
            }

            Debug.Log(
                $"Clip: {clip.name}, " +
                $"Bindings: {bindings.Length}, " +
                $"Missing paths: {missing}"
            );
        }
    }
}