using UnityEngine;

public class Player2Controller : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 3f;
    
    [Header("References")]
    public Transform opponent; // drag P1 here in the Inspector
    
    private bool isAttacking = false;
    
    void Update()
    {
        HandleMovement();
        HandleAttacks();
    }
    
    void HandleMovement()
    {
        // Don't move while attacking
        if (isAttacking) return;
        
        // Numpad 1 = walk forward (toward P1)
        // Numpad 3 = walk backward (away from P1)
        
        Vector3 toOpponent = (opponent.position - transform.position).normalized;
        toOpponent.y = 0; // ignore vertical
        
        if (Input.GetKey(KeyCode.Keypad1))
        {
            // Walk forward = toward opponent
            transform.position += toOpponent * walkSpeed * Time.deltaTime;
        }
        else if (Input.GetKey(KeyCode.Keypad3))
        {
            // Walk backward = away from opponent
            transform.position -= toOpponent * walkSpeed * Time.deltaTime;
        }
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
        // TODO: trigger your attack logic here
        // - play animation
        // - enable hitbox
        // - apply damage to P1 if hit
        
        Invoke(nameof(EndAttack), 0.5f); // adjust duration to match animation
    }
    
    void DoMediumAttack()
    {
        isAttacking = true;
        Debug.Log("[P2] Medium Attack");
        // TODO: trigger your attack logic here
        
        Invoke(nameof(EndAttack), 0.7f);
    }
    
    void DoHeavyAttack()
    {
        isAttacking = true;
        Debug.Log("[P2] Heavy Attack");
        // TODO: trigger your attack logic here
        
        Invoke(nameof(EndAttack), 1.0f);
    }
    
    void EndAttack()
    {
        isAttacking = false;
    }
}