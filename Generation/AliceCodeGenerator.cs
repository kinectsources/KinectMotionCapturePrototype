﻿using Microsoft.Kinect;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.Samples.Kinect.SkeletonBasics
{
	/// <summary>
	/// Generates code for Alice animation methods from Kinect skeletons,
	/// adjusting for Alice bone lengths to produce accurate animations.
	/// </summary>
	class AliceCodeGenerator
	{
		/// <summary>
		/// Helper class for holding data to calculate skeleton rotations.
		/// </summary>
		// See boneData[] below.
		private class JointData
		{
			public string AliceJointFrom { get; set; }
			public JointType KinectJointFrom { get; set; }
			public JointType KinectJointTo { get; set; }
			public float JointLength { get; set; }

			public JointData(string aliceJointFrom, JointType kinectJointFrom, JointType kinectJointTo, float jointLength)
			{
				this.AliceJointFrom = aliceJointFrom;
				this.KinectJointFrom = kinectJointFrom;
				this.KinectJointTo = kinectJointTo;
				this.JointLength = jointLength;
			}
		}

		/// <summary>
		/// String of Alice joint names to ignore when calculating bone rotations.
		/// These are mostly joints that cause the Biped to become too distorted in a recording.
		/// </summary>
		private string ignore = "Neck Pelvis LeftAnkle RightAnkle";

		/// <summary>
		/// The data in this array specifies (1) the order in which the joints are processed
		/// and (2) the length of each bone for improved animation accuracy when translating the movements to Alice.
		/// </summary>
		private JointData[] boneData = new JointData[16] {
			new JointData("SpineBase", JointType.HipCenter, JointType.ShoulderCenter, 0.4375f),
			new JointData("Neck", JointType.ShoulderCenter, JointType.Head, 0.0625f), 
			new JointData("Neck", JointType.ShoulderCenter, JointType.ShoulderLeft, 0.125f),
			new JointData("Neck", JointType.ShoulderCenter, JointType.ShoulderRight, 0.125f),
			new JointData("LeftShoulder", JointType.ShoulderLeft, JointType.ElbowLeft, 0.25f),
			new JointData("RightShoulder", JointType.ShoulderRight, JointType.ElbowRight, 0.25f),
			new JointData("LeftElbow", JointType.ElbowLeft, JointType.HandLeft, 0.125f),
			new JointData("RightElbow", JointType.ElbowRight, JointType.HandRight, 0.125f),
			new JointData("Pelvis", JointType.HipCenter, JointType.HipLeft, 0.0875f),
			new JointData("Pelvis", JointType.HipCenter, JointType.HipRight, 0.0875f),
			new JointData("LeftHip", JointType.HipLeft, JointType.KneeLeft, 0.5f),
			new JointData("RightHip", JointType.HipRight, JointType.KneeRight, 0.5f),
			new JointData("LeftKnee", JointType.KneeLeft, JointType.FootLeft, 0.4375f),
			new JointData("RightKnee", JointType.KneeRight, JointType.FootRight, 0.4375f),
			new JointData("LeftAnkle", JointType.AnkleLeft, JointType.FootLeft, 0.125f),
			new JointData("RightAnkle", JointType.AnkleRight, JointType.FootRight, 0.125f),
		};

		/// <summary>
		/// Stores the final positions of joints so the absolute positions of joints can be computed without recomputing parent joints.
		/// </summary>
		private static Dictionary<JointType, SkeletonPoint> finalPositions = new Dictionary<JointType, SkeletonPoint>();
		/// <summary>
		/// The original position of a joint in the Kinect skeleton that will play the role of root joint,
		/// from which translations of the entire Alice Biped are calculated in #GetMovementCode().
		/// </summary>
		private SkeletonPoint originalRootPosition;
		/// <summary>
		/// Flag indicating whether the original root position has been set already (SkeletonPoint is a struct, so this can't be accomplished with a null check).
		/// </summary>
		private bool originalRootPositionSet;

		public AliceCodeGenerator()
		{
			// seed the dictionary
			for (var i = JointType.HipCenter; i < JointType.FootRight; i++)
			{
				finalPositions.Add(i, new SkeletonPoint());
			}
			// set the final position of the root joint
			finalPositions[JointType.HipCenter] = new SkeletonPoint() { Y = 1.0f };
		}

		public void Init()
		{
			originalRootPositionSet = false;
		}

		/// <summary>
		/// Generates Alice code for moving the entire Biped (allows the Biped to bounce a little during a walking animation, for example).
		/// </summary>
		/// <param name="skeleton">The Kinect skeleton to calculate movements from</param>
		/// <returns>A string of Alice code that executes a Biped's movements</returns>
		public string GetMovementCode(Skeleton skeleton)
		{
			StringBuilder result = new StringBuilder();

			// get the pelvis
			var pelvis = skeleton.Joints.First(joint => joint.JointType == JointType.HipCenter).Position;
			// set the pelvis as the original root if it hasn't been set yet (should happen on the first frame after init() only)
			if (!originalRootPositionSet)
			{
				originalRootPosition = pelvis;
				originalRootPositionSet = true;
			}
			// get the position difference between the original root position and the current position of the root
			var moveDifference = pelvis.Subtract(originalRootPosition).Normalize().Multiply(0.25f);

			// this moving algorithm attempts to use absolute positioning rather than relative positioning (adjusting based on the previous position)
			// in order to stop Alice characters from drifting away after the animation has run a few iterations
			// 1. move a box that does not have the biped as a vehicle to the desired position
			result.Append(string.Format("root.setPositionRelativeToVehicle(new Position(0, {0}, 0), Move.duration(0));", moveDifference.Y));
			// 2. move the person to the box
			result.Append("biped.moveTo(root, MoveTo.duration(0));\n");

			return result.ToString();
		}

		/// <summary>
		/// Generates Alice code for rotating a Biped's joints using another object present in the scene.
		/// The object is placed at each of the skeleton's joints, and then in Alice the parent joint is is pointed at the object to create the desired rotation.
		/// </summary>
		/// <param name="skeleton">The Kinect skeleton to calculate joint rotations from</param>
		/// <returns>A string of Alice code that rotates the joints into their final positions according to the provided Kinect skeleton</returns>
		public string GetJointsCode(Skeleton skeleton)
		{
			// compute the final positions for each joint and add the Alice code to the stringbuilder
			SkeletonPoint fromJoint;
			SkeletonPoint toJoint;
			SkeletonPoint adjustedDifference;
			StringBuilder result = new StringBuilder();

			for (int i = 0; i < boneData.Length; i++)
			{
				fromJoint = skeleton.Joints.First(joint => joint.JointType == boneData[i].KinectJointFrom).Position;
				toJoint = skeleton.Joints.First(joint => joint.JointType == boneData[i].KinectJointTo).Position;
				// adjust the position difference between the two joints so that it matches the length of the Alice joint
				// to do this,
				//		(1) get the difference [subtract],
				//		(2) get a unit vector [normalize], and
				//		(3) multiply by the length of the bone
				adjustedDifference = toJoint
										.Subtract(fromJoint)
										.Normalize()
										.Multiply(boneData[i].JointLength);
				// the adjusted difference needs to be added to the final position of the 'from' joint,
				// which is stored in a dictionary and has been computed already if the values in boneData are ordered correctly
				// ~ it's kind of a dynamic programming approach in that it avoids repeated calculation of the previous joints
				var finalPosition = finalPositions[boneData[i].KinectJointFrom].Add(adjustedDifference);
				finalPositions[boneData[i].KinectJointTo] = finalPosition;

				// add the Alice code to the stringbuilder
				// (ignore rotations from the neck and pelvis for now, which screw stuff up)
				if (!ignore.Contains(boneData[i].AliceJointFrom))
				{
					result.Append(string.Format("box.setPositionRelativeToVehicle(new Position({0}, {1}, {2}), Move.duration(0));biped.get{3}().pointAt(box, PointAt.duration(0));\n",
						finalPosition.X, finalPosition.Y, finalPosition.Z,
						boneData[i].AliceJointFrom
					));
					// if the final joint position was behind its parent joint, the parent needs to be rolled 180 degrees (0.5 rotations)
					// otherwise the joint gets turned around and everything looks weird (twisted spines and knees and such)
					if (adjustedDifference.Z > 0)
					{
						result.Append(string.Format("biped.get{0}().roll(RollDirection.LEFT, 0.5, Roll.duration(0));\n", boneData[i].AliceJointFrom));
					}
				}
			}
			return result.ToString();
		}

	}
}
