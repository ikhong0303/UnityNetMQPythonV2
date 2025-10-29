using UnityEngine;

public class SetRandomIndex : StateMachineBehaviour
{
    // 인스펙터에서 설정할 변수들
    public string parameterName = "ListenIndex"; // 우리가 만든 Int 파라미터 이름
    public int maxCount = 3; // 애니메이션 개수 (Listen은 3개)

    // 이 주머니(Sub-State Machine)로 들어올 때 호출됨
    override public void OnStateMachineEnter(Animator animator, int stateMachinePathHash)
    {
        // 0부터 (개수-1) 사이의 랜덤한 정수를 뽑음 (예: 0, 1, 2 중 하나)
        int randomIndex = Random.Range(0, maxCount);

        // 애니메이터의 Int 파라미터 값을 방금 뽑은 랜덤 값으로 설정
        animator.SetInteger(parameterName, randomIndex);
        Debug.Log($"{parameterName} 값을 {randomIndex} (으)로 설정!");
    }
}