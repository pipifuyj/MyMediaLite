// Copyright (C) 2010 Zeno Gantner
//
// This file is part of MyMediaLite.
//
// MyMediaLite is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// MyMediaLite is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with MyMediaLite.  If not, see <http://www.gnu.org/licenses/>.

using System;
using MyMediaLite.correlation;


namespace MyMediaLite.rating_predictor
{
	/// <summary>
	/// User-based kNN with cosine similarity
	/// </summary>
	public class UserKNNCosine : UserKNN
	{
        /// <inheritdoc />
        public override void Train()
        {
			base.Train();
			this.correlation = Cosine.Create(data_user);
        }

		/// <inheritdoc />
		protected override void RetrainUser(int user_id)
		{
			base.RetrainUser(user_id);
			if (UpdateUsers)
				for (int i = 0; i <= MaxUserID; i++)
					correlation[user_id, i] = Cosine.ComputeCorrelation(data_user[user_id], data_user[i]);
		}

        /// <inheritdoc />
		public override string ToString()
		{
			return string.Format("user-kNN-cosine k={0} reg_u={1} reg_i={2}",
			                     K == uint.MaxValue ? "inf" : K.ToString(), reg_u, reg_i);
		}
	}
}