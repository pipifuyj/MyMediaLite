// Copyright (C) 2010 Steffen Rendle, Zeno Gantner
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
using System.Globalization;
using MyMediaLite.Data;
using MyMediaLite.DataType;


namespace MyMediaLite.RatingPredictor
{
	/// <summary>Social-network-aware matrix factorization</summary>
	/// <remarks>
	/// This implementation assumes a binary and symmetrical trust network.
	/// 
	/// <inproceedings>
	///   <author>Mohsen Jamali</author> <author>Martin Ester</author>
    ///   <title>A matrix factorization technique with trust propagation for recommendation in social networks</title>
    ///   <booktitle>RecSys '10: Proceedings of the Fourth ACM Conference on Recommender Systems</booktitle>
    ///   <year>2010</year>
    /// </inproceedings>
	/// </remarks>
	public class SocialMF : BiasedMatrixFactorization, IUserRelationAwareRecommender
	{
        /// <summary>Social network regularization constant</summary>
		public double SocialRegularization { get { return social_regularization;	} set {	social_regularization = value; } }
        private double social_regularization = 1;

		/*
		/// <summary>
		/// Use stochastic gradient descent instead of batch gradient descent
		/// </summary>
		public bool StochasticLearning { get; set; }
		*/

		/// <inheritdoc/>
		public SparseBooleanMatrix UserRelation
		{
			get { return this.user_neighbors; }
			set
			{
				this.user_neighbors = value;
				this.MaxUserID = Math.Max(MaxUserID, user_neighbors.NumberOfRows - 1);
				this.MaxUserID = Math.Max(MaxUserID, user_neighbors.NumberOfColumns - 1);
			}
		}
		private SparseBooleanMatrix user_neighbors;

		/// <summary>the number of users</summary>
		public int NumUsers { get { return MaxUserID + 1; } }

		/// <inheritdoc/>
        public override void Train()
		{
			// init latent factor matrices
	       	user_factors = new Matrix<double>(NumUsers, num_factors);
	       	item_factors = new Matrix<double>(ratings.MaxItemID + 1, num_factors);
	       	MatrixUtils.InitNormal(user_factors, InitMean, InitStdev);
	       	MatrixUtils.InitNormal(item_factors, InitMean, InitStdev);
			// init biases
			user_bias = new double[NumUsers];
			item_bias = new double[ratings.MaxItemID + 1];
			
			Console.Error.WriteLine("num_users={0}, num_items={1}", NumUsers, item_bias.Length);
			
			// compute global average
			double global_average = 0;
			foreach (RatingEvent r in Ratings.All)
				global_average += r.rating;
			global_average /= Ratings.All.Count;

			// learn model parameters
            global_bias = Math.Log( (global_average - MinRating) / (MaxRating - global_average) );
            for (int current_iter = 0; current_iter < NumIter; current_iter++)
				Iterate(ratings.All, true, true);
		}

		/// <inheritdoc/>
		protected override void Iterate(Ratings ratings, bool update_user, bool update_item)
		{
			IterateBatch();
		}

		private void IterateBatch()
		{
			// I. compute gradients
			var user_factors_gradient = new Matrix<double>(user_factors.dim1, user_factors.dim2);
			var item_factors_gradient = new Matrix<double>(item_factors.dim1, item_factors.dim2);
			var user_bias_gradient    = new double[user_factors.dim1];
			var item_bias_gradient    = new double[item_factors.dim1];

			// I.1 prediction error
			double rating_range_size = MaxRating - MinRating;
			foreach (RatingEvent rating in ratings)
            {
            	int u = rating.user_id;
                int i = rating.item_id;

				// prediction
				double score = global_bias;
				score += user_bias[u];
				score += item_bias[i];
	            for (int f = 0; f < num_factors; f++)
    	            score += user_factors[u, f] * item_factors[i, f];
				double sig_score = 1 / (1 + Math.Exp(-score));

                double prediction = MinRating + sig_score * rating_range_size;
				double error      = rating.rating - prediction;

				double gradient_common = error * sig_score * (1 - sig_score) * rating_range_size;

				// add up error gradient
                for (int f = 0; f < num_factors; f++)
                {
                 	double u_f = user_factors[u, f];
                    double i_f = item_factors[i, f];

                    if (f != 0)
						MatrixUtils.Inc(user_factors_gradient, u, f, gradient_common * i_f);
                    if (f != 1)
						MatrixUtils.Inc(item_factors_gradient, i, f, gradient_common * u_f);
                }
			}

			// I.2 L2 regularization
			//        biases
			for (int u = 0; u < user_bias_gradient.Length; u++)
				user_bias_gradient[u] += user_bias[u] * regularization;
			for (int i = 0; i < item_bias_gradient.Length; i++)
				item_bias_gradient[i] += item_bias[i] * regularization;
			//        latent factors
			for (int u = 0; u < user_factors_gradient.dim1; u++)
				for (int f = 2; f < num_factors; f++)
					MatrixUtils.Inc(user_factors_gradient, u, f, user_factors[u, f] * regularization);

			for (int i = 0; i < item_factors_gradient.dim1; i++)
				for (int f = 2; f < num_factors; f++)
					MatrixUtils.Inc(item_factors_gradient, i, f, item_factors[i, f] * regularization);

			// I.3 social network regularization
			for (int u = 0; u < user_factors_gradient.dim1; u++)
			{
				// see eq. (13) in the paper
				double[] sum_neighbors    = new double[num_factors];
				double bias_sum_neighbors = 0;
				int      num_neighbors    = user_neighbors[u].Count;
				
				// user bias part
				foreach (int v in user_neighbors[u])
					bias_sum_neighbors += user_bias[v];
				if (num_neighbors != 0)
					user_bias_gradient[u] += social_regularization * (user_bias[u] - bias_sum_neighbors / num_neighbors);
				foreach (int v in user_neighbors[u])
					if (user_neighbors[v].Count != 0)
					{
						double trust_v = (double) 1 / user_neighbors[v].Count;
						double diff = 0;
						foreach (int w in user_neighbors[v])
							diff -= user_bias[w];
	
						diff = diff * trust_v;
						diff += user_bias[v];
						
						if (num_neighbors != 0)
							user_bias_gradient[u] -= social_regularization * trust_v * diff / num_neighbors;
					}				
				
				// latent factor part
				foreach (int v in user_neighbors[u])
                	for (int f = 0; f < num_factors; f++)
						sum_neighbors[f] += user_factors[v, f];
				if (num_neighbors != 0)
					for (int f = 0; f < num_factors; f++)
						MatrixUtils.Inc(user_factors_gradient, u, f, social_regularization * (user_factors[u, f] - sum_neighbors[f] / num_neighbors));
				foreach (int v in user_neighbors[u])
					if (user_neighbors[v].Count != 0)
					{
						double trust_v = (double) 1 / user_neighbors[v].Count;					
						for (int f = 0; f < num_factors; f++)
						{
							double diff = 0;
							foreach (int w in user_neighbors[v])
								diff -= user_factors[w, f];
							diff = diff * trust_v;
							diff += user_factors[v, f];
							if (num_neighbors != 0)
								MatrixUtils.Inc(user_factors_gradient, u, f, -social_regularization * trust_v * diff / num_neighbors);
						}
					}
			}

			// II. apply gradient descent step
			for (int u = 0; u < user_factors_gradient.dim1; u++)
			{
				user_bias[u] += user_bias_gradient[u] * learn_rate;
				for (int f = 2; f < num_factors; f++)
					MatrixUtils.Inc(user_factors, u, f, user_factors_gradient[u, f] * learn_rate);
			}
			for (int i = 0; i < item_factors_gradient.dim1; i++)
			{
				item_bias[i] += item_bias_gradient[i] * learn_rate;				
				for (int f = 2; f < num_factors; f++)
					MatrixUtils.Inc(item_factors, i, f, item_factors_gradient[i, f] * learn_rate);
			}
		}

		/// <inheritdoc/>
		public override string ToString()
		{
			NumberFormatInfo ni = new NumberFormatInfo();
			ni.NumberDecimalDigits = '.';

			return string.Format(ni,
			                     "SocialMF num_factors={0} regularization={1} social_regularization={2} learn_rate={3} num_iter={4} init_mean={5} init_stdev={6}",
				                 NumFactors, Regularization, SocialRegularization, LearnRate, NumIter, InitMean, InitStdev);
		}
	}
}