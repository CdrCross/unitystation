﻿using System;
using UnityEngine;
using Mirror;

/// <summary>
/// A machine into which players can insert certain food items.
/// After some time the food is cooked and gets ejected from the microwave.
/// </summary>
public class Microwave : NetworkBehaviour
{
	/// <summary>
	/// Time it takes for the microwave to cook a meal (in seconds).
	/// </summary>
	[Range(0, 60)]
	public float COOK_TIME = 10;

	/// <summary>
	/// Time left until the meal has finished cooking (in seconds).
	/// I don't see the need to make it a SyncVar.  It will economize network packets.
	/// </summary>
	[HideInInspector]
	public float MicrowaveTimer = 0;

	/// <summary>
	/// Meal currently being cooked.
	/// </summary>
	[HideInInspector]
	public string meal;

	/// <summary>
	/// When a stackable food goes into the microwave, hold onto the stack number so that stack.amount in = stack.amount out
	/// </summary>
	[HideInInspector]
	public int mealCount = 0;


	// Sprites for when the microwave is on or off.
	public Sprite SPRITE_ON;
	private Sprite SPRITE_OFF;

	private SpriteRenderer spriteRenderer;

	/// <summary>
	/// AudioSource for playing the celebratory "ding" when cooking is finished.
	/// </summary>
	private AudioSource audioSourceDing;

	/// <summary>
	/// Amount of time the "ding" audio source will start playing before the microwave has finished cooking (in seconds).
	/// </summary>
	private static float dingPlayTime = 1.54f;

	/// <summary>
	/// True if the microwave has already played the "ding" sound for the current meal.
	/// </summary>
	private bool dingHasPlayed = false;

	/// <summary>
	/// Set up the microwave sprite and the AudioSource.
	/// </summary>
	private void Start()
	{
		spriteRenderer = GetComponentInChildren<SpriteRenderer>();
		audioSourceDing = GetComponent<AudioSource>();
		SPRITE_OFF = spriteRenderer.sprite;
	}

	private void OnEnable()
	{
		UpdateManager.Add(CallbackType.UPDATE,UpdateMe);
	}

	private void OnDisable()
	{
		UpdateManager.Remove(CallbackType.UPDATE,UpdateMe);
	}

	/// <summary>
	/// Count remaining time to microwave previously inserted food.
	/// </summary>
	private void UpdateMe()
	{
		if (MicrowaveTimer > 0)
		{
			MicrowaveTimer = Mathf.Max(0, MicrowaveTimer - Time.deltaTime);

			if (!dingHasPlayed && MicrowaveTimer <= dingPlayTime)
			{
				audioSourceDing.Play();
				dingHasPlayed = true;
			}

			if (MicrowaveTimer <= 0)
			{
				FinishCooking();
			}
		}
	}

	public void ServerSetOutputMeal(string mealName)
	{
		meal = mealName;
	}

		public void ServerSetOutputStackAmount(int stackCount)
	{
		mealCount = stackCount;
	}

	/// <summary>
	/// Starts the microwave, cooking the food for {MicrowaveTimer} seconds.
	/// </summary>
	[ClientRpc]
	public void RpcStartCooking()
	{
		MicrowaveTimer = COOK_TIME;
		spriteRenderer.sprite = SPRITE_ON;
		dingHasPlayed = false;
	}

	/// <summary>
	/// Finish cooking the microwaved meal.
	/// </summary>
	private void FinishCooking()
	{
		spriteRenderer.sprite = SPRITE_OFF;

		if (isServer && mealCount > 0)
		{
			GameObject mealPrefab = CraftingManager.Meals.FindOutputMeal(meal);
			Vector3Int spawnPosition = GetComponent<RegisterTile>().WorldPosition;

			SpawnResult result = Spawn.ServerPrefab(mealPrefab, spawnPosition, transform.parent);
			Stackable stck = result.GameObject.GetComponent<Stackable>();

			if (stck != null)   // If the meal has a stackable component, set the correct number of meals
			{
				int spawnedSoFar = stck.InitialAmount;

				while (spawnedSoFar < mealCount)
				{
					int spaceInStack = stck.MaxAmount - stck.Amount;

					if (spaceInStack > 0)
					{
						// Add all the missing meals to the stack, or as many as the stack can contain
						int mealDeficit = mealCount - spawnedSoFar;
						int stacksToAdd = Mathf.Min(spaceInStack, mealDeficit);
						stck.ServerIncrease(stacksToAdd);
						spawnedSoFar += stacksToAdd;
					}

					if (spawnedSoFar < mealCount) // Still not enough meals
					{
						result = Spawn.ServerPrefab(mealPrefab, spawnPosition, transform.parent);
						stck = result.GameObject.GetComponent<Stackable>();
						spawnedSoFar += stck.InitialAmount;
					}
				}

				if (spawnedSoFar > mealCount) // Reduce the stack if our last spawned stackable results in too many meals
				{
					int mealSurplus = spawnedSoFar - mealCount;
					stck.ServerConsume(mealSurplus);
				}
			}
			else if (mealCount > 1) // Spawn non-stackable meals
			{
				Spawn.ServerPrefab(mealPrefab, spawnPosition, transform.parent, count: mealCount - 1);
			}
		}
		meal = null;
		mealCount = 0;
	}

}