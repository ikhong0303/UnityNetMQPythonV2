using UnityEngine;

// --- 👇 여기에 드롭다운 메뉴에 표시될 항목들을 정의합니다 ---
public enum AnimationParameterType
{
    IdleIndex,
    ListenIndex,
    TalkIndex
}

public class SetRandomIndex : StateMachineBehaviour
{
    // --- 👇 string 대신 enum 타입을 사용합니다 ---
    [Tooltip("랜덤 값을 설정할 Int 파라미터를 선택하세요.")]
    public AnimationParameterType parameter = AnimationParameterType.ListenIndex; // 기본값을 ListenIndex로 설정

    [Tooltip("해당 상태의 애니메이션 개수를 입력하세요.")]
    public int maxCount = 3;

    override public void OnStateMachineEnter(Animator animator, int stateMachinePathHash)
    {
        if (maxCount <= 0) return; // 애니메이션 개수가 0 이하면 실행하지 않음

        int randomIndex = Random.Range(0, maxCount);

        // --- 👇 선택된 enum 값을 문자열로 변환하여 사용합니다 ---
        string parameterNameString = parameter.ToString();

        animator.SetInteger(parameterNameString, randomIndex);
        Debug.Log($"{parameterNameString} 값을 {randomIndex} (으)로 설정!");
    }
}