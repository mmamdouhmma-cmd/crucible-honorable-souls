using UnityEngine;

public class Player2Controller : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 3f;
    
    [Header("References")]
    public Transform opponent; // drag P1 here in the Inspector
    public Animator animator;
    
    private bool isAttacking = false;
    
    void Update()
    {
        HandleMovement();
        HandleAttacks();
    }
    
    void HandleMovement()
{
    if (isAttacking)
    {
        animator.SetFloat("movespeed", 0f); // stop walk animation while attacking
        return;
    }
    
    Vector3 toOpponent = (opponent.position - transform.position).normalized;
    toOpponent.y = 0;
    
    float currentSpeed = 0f;
    
    if (Input.GetKey(KeyCode.Keypad1))
    {
        transform.position += toOpponent * walkSpeed * Time.deltaTime;
        currentSpeed = walkSpeed;
    }
   else if (Input.GetKey(KeyCode.Keypad3))
{
    transform.position -= toOpponent * walkSpeed * Time.deltaTime;
    currentSpeed = -walkSpeed; // ← negative value triggers walk-back animation
}

    Debug.Log("Setting MoveSpeed to: " + currentSpeed);
    animator.SetFloat("movespeed", currentSpeed);
}
    
    void HandleAttacks()
    {
        if (isAttacking) return;
        
        if (Input.GetKeyDown(KeyCode.Keypad7))
        {
            DoLightAttack();
        }
        else if (Input.GetKeyDown(KeyCode.Keypad8))
        {
            DoMediumAttack();
        }
        else if (Input.GetKeyDown(KeyCode.Keypad9))
        {
            DoHeavyAttack();
        }
    }
    
    void DoLightAttack()
{
    isAttacking = true;
    Debug.Log("[P2] Light Attack");
    animator.SetTrigger("LightAttack");
    Invoke(nameof(EndAttack), 0.5f);
}

void DoMediumAttack()
{
    isAttacking = true;
    Debug.Log("[P2] Medium Attack");
    animator.SetTrigger("MediumAttack");
    Invoke(nameof(EndAttack), 0.7f);
}

void DoHeavyAttack()
{
    isAttacking = true;
    Debug.Log("[P2] Heavy Attack");
    animator.SetTrigger("HeavyAttack");
    Invoke(nameof(EndAttack), 1.0f);
}
    
    void EndAttack()
    {
        isAttacking = false;
    }
}